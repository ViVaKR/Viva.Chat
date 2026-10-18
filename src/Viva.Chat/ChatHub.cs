using System.ComponentModel;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;
using Viva.Chat.Contracts;

namespace Viva.Chat;

public sealed class ChatHub(IChatStore store, IOptions<VivaChatOptions> options) : Hub<IChatClient>
{
    private const string SiteKey = "viva.site";
    private const string JoinedKey = "viva.joined";
    private readonly VivaChatOptions _opt = options.Value;
    private string Site => (string)Context.Items[SiteKey]!;

    // 도메인 간 방이 섞이지 않도록 "{site}:{room}" 형태로 그룹을 만듬
    private string GroupName(string room) => $"{Site}:{room}";
    private HashSet<string> Joined => (HashSet<string>)Context.Items[JoinedKey]!;

    private string DisplayName =>
      Context.User?.Identity?.Name ?? $"guest-{Context.ConnectionId[..4]}";

    public override Task OnConnectedAsync()
    {
        var site = ResolveSite();
        if (site is null)
        {
            Context.Abort(); // 허용되지 않은 사이트
            return Task.CompletedTask;
        }

        Context.Items[SiteKey] = site;
        Context.Items[JoinedKey] = new HashSet<string>();
        return base.OnConnectedAsync();
    }

    /// <summary>방에 입장하고 최근 대화 이력을 돌려준다.</summary>
    public async Task<IReadOnlyList<ChatMessage>> Join(string room)
    {
        EnsureValidRoom(room);
        await Groups.AddToGroupAsync(Context.ConnectionId, GroupName(room));
        Joined.Add(room);
        await Clients.Group(GroupName(room)).UserJoined(room, DisplayName);
        return await store.GetRecentAsync(Site, room, _opt.HistoryOnJoin, Context.ConnectionAborted);
    }

    public async Task Leave(string room)
    {
        EnsureValidRoom(room);

        await Groups.RemoveFromGroupAsync(Context.ConnectionId, GroupName(room));
        Joined.Remove(room);
        await Clients.Group(GroupName(room)).UserLeft(room, DisplayName);
    }

    public async Task SendMessage(string room, string text)
    {
        EnsureValidRoom(room);

        if (!Joined.Contains(room))
            throw new HubException("먼저 방에 입장해야 합니다.");

        text = text?.Trim() ?? string.Empty;

        if (text.Length == 0 || text.Length > _opt.MaxMessageLength)
            throw new HubException("메시지 길이가 올바르지 않습니다.");

        var message = new ChatMessage(
            Guid.NewGuid().ToString("N"),
            Site,
            room,
            DisplayName,
            text,
            DateTimeOffset.UtcNow
        );

        await store.SaveAsync(message, Context.ConnectionAborted);
        await Clients.Group(GroupName(room)).ReceiveMessage(message);
    }

    private string? ResolveSite()
    {

        var requested = Context.GetHttpContext()?.Request.Query["site"].ToString();
        var site = string.IsNullOrWhiteSpace(requested)
          ? _opt.DefaultSiteId
          : requested.Trim().ToLowerInvariant();

        var allowed = site.Equals(_opt.DefaultSiteId, StringComparison.OrdinalIgnoreCase)
          || _opt.AllowedSites.Contains(site, StringComparer.OrdinalIgnoreCase);

        return allowed ? site : null;
    }

    private static void EnsureValidRoom(string room)
    {
        var ok = !string.IsNullOrWhiteSpace(room)
          && room.Length <= 64
          && room.All(x => char.IsLetterOrDigit(x) || x is '-' or '_');

        if (!ok) throw new HubException("방 이름이 올바르지 않습니다.");
    }
}
