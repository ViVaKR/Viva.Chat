using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Viva.Chat.Contracts;

namespace Viva.Chat.Ai;

/// <summary>
/// AI 채팅 허브. 세션 방식: Open(세션 열기) → Send(질문, 답변은 이벤트로 스트리밍) → Cancel(중단).
/// 로그인한 사용자만 쓸 수 있고, 세션은 사용자별로 분리된다 (방 이름에 사용자 해시가 들어간다).
/// 세션 목록 같은 조회는 허브가 아니라 호스트의 REST 로 제공한다.
/// </summary>
public sealed class AiChatHub(
    IChatStore store,
    IChatGuard guard,
    ISiteResolver siteResolver,
    IChatIdentityResolver identityResolver,
    IAiTurnGate gate,
    AiChatSessionRunner runner,
    IOptions<VivaAiChatOptions> options,
    TimeProvider time,
    ILogger<AiChatHub> logger) : Hub<IAiChatClient>
{
    private const string StateKey = "viva.aichat.state";
    private const int MaxOpenedPerConnection = 20;
    private readonly VivaAiChatOptions _opt = options.Value;

    private sealed class ConnectionState(string site, ChatIdentity identity)
    {
        public string Site { get; } = site;
        public ChatIdentity Identity { get; } = identity;

        /// <summary>OnConnectedAsync 에서 null 이 아님을 보장한다.</summary>
        public string UserId => Identity.UserId!;

        public HashSet<string> Opened { get; } = new(StringComparer.Ordinal);
        public Queue<long> Started { get; } = new();
    }

    private ConnectionState State =>
        Context.Items.TryGetValue(StateKey, out var s) && s is ConnectionState state
            ? state
            : throw new HubException("연결이 초기화되지 않았습니다.");

    public override async Task OnConnectedAsync()
    {
        var site = siteResolver.Resolve(Context);
        var identity = identityResolver.Resolve(Context);

        // AI 는 GPU 를 쓰므로 로그인한 사용자만 허용한다. (손님은 사용자 ID 가 없어서 세션을 나눌 수도 없다)
        if (site is null || !identity.IsAuthenticated || string.IsNullOrEmpty(identity.UserId))
        {
            logger.LogWarning("AI 채팅 연결을 거절했습니다. connection={ConnectionId}", Context.ConnectionId);
            Context.Abort();
            return;
        }

        Context.Items[StateKey] = new ConnectionState(site, identity);
        await base.OnConnectedAsync();
    }

    /// <summary>
    /// 세션을 열고 이력을 돌려준다. sessionId 를 비우면 새 세션을 만든다 (ID 는 서버가 발급).
    /// 재연결하면 그룹 멤버십이 사라지므로, 클라이언트는 onreconnected 에서 같은 sessionId 로 다시 호출해야 한다.
    /// </summary>
    public async Task<AiSessionOpened> Open(string? sessionId = null)
    {
        var state = State;
        var ct = Context.ConnectionAborted;

        var id = string.IsNullOrWhiteSpace(sessionId)
            ? AiRooms.NewSessionId()
            : AiRooms.RequireSessionId(sessionId);

        if (!state.Opened.Contains(id) && state.Opened.Count >= MaxOpenedPerConnection)
            throw new HubException("한 연결에서 열 수 있는 세션 수를 넘었습니다.");

        var room = AiRooms.RoomName(state.UserId, id);
        var group = AiRooms.GroupName(state.Site, room);

        // 그룹에 먼저 들어가서, 이력을 읽는 사이에 시작된 스트림을 놓치지 않는다.
        await Groups.AddToGroupAsync(Context.ConnectionId, group, ct);
        var firstOpen = state.Opened.Add(id);

        IReadOnlyList<ChatMessage> history;
        try
        {
            history = _opt.HistoryOnOpen > 0
                ? await store.GetRecentAsync(state.Site, room, _opt.HistoryOnOpen, ct)
                : [];
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not HubException)
        {
            logger.LogError(ex, "AI 대화 이력을 읽지 못했습니다. site={Site}", state.Site);

            if (firstOpen)
            {
                state.Opened.Remove(id);
                await Groups.RemoveFromGroupAsync(Context.ConnectionId, group, CancellationToken.None);
            }

            throw new HubException("대화 이력을 불러오지 못했습니다. 잠시 후 다시 시도하세요.");
        }

        return new AiSessionOpened(id, history);
    }

    /// <summary>이 연결에서 세션 알림을 그만 받는다. 진행 중인 답변은 멈추지 않고 저장까지 진행된다.</summary>
    public async Task Close(string sessionId)
    {
        var state = State;
        var id = AiRooms.RequireSessionId(sessionId);

        if (!state.Opened.Remove(id)) return;

        var group = AiRooms.GroupName(state.Site, AiRooms.RoomName(state.UserId, id));
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, group);
    }

    /// <summary>
    /// 질문을 보낸다. 바로 돌아오고, 답변은 ReceiveChunk → StreamCompleted(또는 StreamFailed / StreamCancelled) 이벤트로 온다.
    /// 세션당 진행 중인 턴은 하나뿐이다.
    /// </summary>
    public async Task<AiTurnAccepted> Send(string sessionId, string text)
    {
        var state = State;
        var id = AiRooms.RequireSessionId(sessionId);

        if (!state.Opened.Contains(id))
            throw new HubException("먼저 세션을 열어야 합니다.");

        text = text?.Trim() ?? string.Empty;
        if (text.Length == 0 || text.Length > _opt.MaxPromptLength)
            throw new HubException("질문 길이가 올바르지 않습니다.");

        EnsureWithinRate(state);

        var room = AiRooms.RoomName(state.UserId, id);
        var group = AiRooms.GroupName(state.Site, room);

        ChatVerdict verdict;
        try
        {
            verdict = await guard.InspectAsync(state.Identity.ToCaller(), state.Site, room, text, Context.ConnectionAborted);
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not HubException)
        {
            // 질문 본문은 개인적인 내용이라 로그에 남기지 않는다.
            logger.LogError(ex, "질문 검사 중 오류가 났습니다. site={Site}", state.Site);
            throw new HubException("질문을 검사하지 못했습니다. 잠시 후 다시 시도하세요.");
        }

        if (!verdict.Allowed)
            throw new HubException(verdict.Reason ?? "허용되지 않는 질문입니다.");

        // 질문을 저장하기 전에 자리부터 잡는다. (거절된 질문이 이력에 남지 않게)
        var turn = await gate.TryBeginAsync(group, Context.ConnectionAborted);
        if (turn is null)
            throw new HubException("이전 답변이 아직 진행 중입니다. 끝난 뒤에 다시 보내세요.");

        ChatMessage userMessage;
        try
        {
            var now = time.GetUtcNow();
            userMessage = new ChatMessage(
                Guid.CreateVersion7(now).ToString("N"),
                state.Site,
                room,
                state.Identity.DisplayName,
                verdict.Text ?? text,
                now,
                state.Identity.UserId,
                state.Identity.Avatar);

            await store.SaveAsync(userMessage, CancellationToken.None);
            await Clients.Group(group).ReceiveMessage(id, userMessage);
        }
        catch (Exception ex)
        {
            await turn.DisposeAsync();
            logger.LogError(ex, "질문을 처리하지 못했습니다. site={Site}", state.Site);
            throw new HubException("질문을 처리하지 못했습니다. 잠시 후 다시 시도하세요.");
        }

        var streamId = Guid.CreateVersion7(time.GetUtcNow()).ToString("N");

        // 여기부터 turn 의 주인은 러너다 (끝나면 러너가 반납한다).
        runner.Start(new AiTurnRequest(state.Site, room, id, group, userMessage, streamId), turn);

        return new AiTurnAccepted(userMessage.Id, streamId);
    }

    /// <summary>진행 중인 답변을 중단한다. 중단할 것이 없으면 false.</summary>
    public bool Cancel(string sessionId)
    {
        var state = State;
        var id = AiRooms.RequireSessionId(sessionId);

        if (!state.Opened.Contains(id))
            throw new HubException("먼저 세션을 열어야 합니다.");

        return gate.Cancel(AiRooms.GroupName(state.Site, AiRooms.RoomName(state.UserId, id)));
    }

    /// <summary>연결 단위 턴 시작 속도 제한 (사용자 단위는 Redis 등이 필요하므로 호스트의 몫)</summary>
    private void EnsureWithinRate(ConnectionState state)
    {
        if (_opt.MaxTurnsPerMinute <= 0) return;

        var now = time.GetTimestamp();
        var window = TimeSpan.FromMinutes(1);

        while (state.Started.Count > 0 && time.GetElapsedTime(state.Started.Peek(), now) > window)
            state.Started.Dequeue();

        if (state.Started.Count >= _opt.MaxTurnsPerMinute)
            throw new HubException("질문을 너무 빠르게 보내고 있습니다. 잠시 후 다시 시도하세요.");

        state.Started.Enqueue(now);
    }
}
