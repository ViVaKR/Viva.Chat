using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Viva.Chat.Contracts;

namespace Viva.Chat;

public sealed class ChatHub(
    IChatStore store,
    IChatGuard guard,
    ISiteResolver siteResolver,
    IChatIdentityResolver identityResolver,
    IOptions<VivaChatOptions> options,
    TimeProvider time,
    ILogger<ChatHub> logger) : Hub<IChatClient>
{
    private const string StateKey = "viva.chat.state";
    private readonly VivaChatOptions _opt = options.Value;

    /// <summary>연결 하나가 가지는 상태 (사이트, 누구인가, 입장한 방, 최근 전송 시각)</summary>
    private sealed class ConnectionState(string site, ChatIdentity identity)
    {
        public string Site { get; } = site;
        public ChatIdentity Identity { get; } = identity;
        public HashSet<string> Joined { get; } = new(StringComparer.Ordinal);
        public Queue<long> Sent { get; } = new();
    }

    private ConnectionState State =>
        Context.Items.TryGetValue(StateKey, out var s) && s is ConnectionState state
            ? state
            : throw new HubException("연결이 초기화되지 않았습니다.");

    // 도메인 간 방이 섞이지 않도록 "{site}:{room}" 형태로 그룹을 만듬
    private static string GroupName(string site, string room) => $"{site}:{room}";

    public override async Task OnConnectedAsync()
    {
        var site = siteResolver.Resolve(Context);
        if (site is null)
        {
            // 거절 이유가 로그에 남아야 운영에서 원인을 찾을 수 있다 (사용자 입력 값은 남기지 않는다).
            logger.LogWarning("허용되지 않는 사이트라서 연결을 거절했습니다. connection={ConnectionId}", Context.ConnectionId);
            Context.Abort();
            return;
        }

        // 이름과 아바타는 연결할 때 한 번만 해석한다 (JWT 스냅샷).
        Context.Items[StateKey] = new ConnectionState(site, identityResolver.Resolve(Context));
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        try
        {
            if (_opt.AnnouncePresence
                && Context.Items.TryGetValue(StateKey, out var s)
                && s is ConnectionState state)
            {
                var who = state.Identity.ToParticipant();
                foreach (var room in state.Joined)
                    await Clients.GroupExcept(GroupName(state.Site, room), [Context.ConnectionId]).UserLeft(room, who);
            }
        }
        finally
        {
            await base.OnDisconnectedAsync(exception);
        }
    }

    /// <summary>방에 입장하고 최근 대화 이력을 돌려준다. 같은 방에 다시 Join 해도 입장 알림은 한 번만 간다.</summary>
    public async Task<IReadOnlyList<ChatMessage>> Join(string room)
    {
        EnsureValidRoom(room);

        var state = State;
        var ct = Context.ConnectionAborted;

        bool allowed;
        try
        {
            allowed = await guard.CanJoinAsync(state.Identity.ToCaller(), state.Site, room, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not HubException)
        {
            logger.LogError(ex, "입장 검사 중 오류가 났습니다. site={Site} room={Room}", state.Site, room);
            throw new HubException("입장을 확인하지 못했습니다. 잠시 후 다시 시도하세요.");
        }

        if (!allowed)
            throw new HubException("입장할 수 없는 방입니다.");

        var group = GroupName(state.Site, room);
        await Groups.AddToGroupAsync(Context.ConnectionId, group, ct);
        var firstJoin = state.Joined.Add(room);

        // 이력을 먼저 읽어서, 실패하면 입장 알림이 나가기 전에 되돌린다.
        // (그룹에 먼저 들어가므로 이력을 읽는 사이에 온 메시지를 놓치지 않는다. 겹치는 메시지는 클라이언트가 Id 로 걸러낸다.)
        IReadOnlyList<ChatMessage> history;
        try
        {
            history = await store.GetRecentAsync(state.Site, room, _opt.HistoryOnJoin, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not HubException)
        {
            logger.LogError(ex, "대화 이력을 읽지 못했습니다. site={Site} room={Room}", state.Site, room);

            if (firstJoin)
            {
                state.Joined.Remove(room);
                await Groups.RemoveFromGroupAsync(Context.ConnectionId, group, CancellationToken.None);
            }

            throw new HubException("대화 이력을 불러오지 못했습니다. 잠시 후 다시 시도하세요.");
        }

        if (firstJoin && _opt.AnnouncePresence)
            await Clients.Group(group).UserJoined(room, state.Identity.ToParticipant());

        return history;
    }

    public async Task Leave(string room)
    {
        EnsureValidRoom(room);

        var state = State;
        var group = GroupName(state.Site, room);

        await Groups.RemoveFromGroupAsync(Context.ConnectionId, group);

        if (state.Joined.Remove(room) && _opt.AnnouncePresence)
            await Clients.Group(group).UserLeft(room, state.Identity.ToParticipant());
    }

    public async Task SendMessage(string room, string text)
    {
        EnsureValidRoom(room);

        var state = State;
        if (!state.Joined.Contains(room))
            throw new HubException("먼저 방에 입장해야 합니다.");

        text = text?.Trim() ?? string.Empty;

        if (text.Length == 0 || text.Length > _opt.MaxMessageLength)
            throw new HubException("메시지 길이가 올바르지 않습니다.");

        EnsureWithinRate(state);

        ChatVerdict verdict;
        try
        {
            verdict = await guard.InspectAsync(state.Identity.ToCaller(), state.Site, room, text, Context.ConnectionAborted);
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not HubException)
        {
            // 메시지 본문은 개인적인 내용이라 로그에 남기지 않는다.
            logger.LogError(ex, "메시지 검사 중 오류가 났습니다. site={Site} room={Room}", state.Site, room);
            throw new HubException("메시지를 검사하지 못했습니다. 잠시 후 다시 시도하세요.");
        }

        if (!verdict.Allowed)
            throw new HubException(verdict.Reason ?? "허용되지 않는 메시지입니다.");

        var now = time.GetUtcNow();
        var message = new ChatMessage(
            Guid.CreateVersion7(now).ToString("N"), // 시간순 정렬되는 ID (인덱스 지역성에 유리)
            state.Site,
            room,
            state.Identity.DisplayName,
            verdict.Text ?? text,
            now,
            state.Identity.UserId,
            state.Identity.Avatar
        );

        // 보낸 사람이 끊겨도 이미 접수한 메시지는 저장·전달한다.
        // 저장에 실패한 메시지는 방에 전달하지 않는다 (보였는데 이력에는 없는 메시지를 막기 위해).
        try
        {
            await store.SaveAsync(message, CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "메시지를 저장하지 못했습니다. site={Site} room={Room}", state.Site, room);
            throw new HubException("메시지를 저장하지 못했습니다. 잠시 후 다시 시도하세요.");
        }

        await Clients.Group(GroupName(state.Site, room)).ReceiveMessage(message);
    }

    /// <summary>연결 단위 전송 속도 제한 (사용자 단위는 Redis 등이 필요하므로 호스트의 몫)</summary>
    private void EnsureWithinRate(ConnectionState state)
    {
        if (_opt.MaxMessagesPerMinute <= 0) return;

        var now = time.GetTimestamp();
        var window = TimeSpan.FromMinutes(1);

        while (state.Sent.Count > 0 && time.GetElapsedTime(state.Sent.Peek(), now) > window)
            state.Sent.Dequeue();

        if (state.Sent.Count >= _opt.MaxMessagesPerMinute)
            throw new HubException("메시지를 너무 빠르게 보내고 있습니다. 잠시 후 다시 시도하세요.");

        state.Sent.Enqueue(now);
    }

    private static void EnsureValidRoom(string room)
    {
        var ok = !string.IsNullOrWhiteSpace(room)
          && room.Length <= 64
          && room.All(x => char.IsLetterOrDigit(x) || x is '-' or '_');

        if (!ok) throw new HubException("방 이름이 올바르지 않습니다.");
    }
}
