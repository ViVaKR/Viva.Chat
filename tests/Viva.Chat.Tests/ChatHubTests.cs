using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR;

namespace Viva.Chat.Tests;

/// <summary>실제 허브를 메모리 서버에 띄워서, 클라이언트 두세 명이 주고받는 흐름을 시험한다.</summary>
public class ChatHubTests
{
    private const string Seeker = "sub=u-1;preferred_username=Seeker;picture=/avatars/a.png;name=RealName;email=real@example.com;role=admin";
    private const string Walker = "sub=u-2;preferred_username=Walker";

    // ── 기본 흐름 ─────────────────────────────────────────────

    [Fact(DisplayName = "입장하고 보내면 같은 방의 다른 사람이 받는다")]
    public async Task Join_send_receive()
    {
        await using var host = await ChatTestHost.StartAsync();
        var a = await host.ConnectAsync(Seeker);
        var b = await host.ConnectAsync(Walker);

        await a.JoinAsync("lobby");
        await b.JoinAsync("lobby");
        await a.SendAsync("lobby", "안녕하세요");

        var received = await b.Messages.NextAsync();
        Assert.Equal("안녕하세요", received.Text);
        Assert.Equal("lobby", received.Room);
        Assert.Equal("test", received.SiteId);

        // 보낸 사람도 자기 메시지를 받는다
        Assert.Equal("안녕하세요", (await a.Messages.NextAsync()).Text);
    }

    [Fact(DisplayName = "메시지에 필명, 사용자 ID, 아바타가 담기고 실명과 이메일은 새지 않는다")]
    public async Task Message_carries_pseudonym_userid_avatar()
    {
        await using var host = await ChatTestHost.StartAsync();
        var a = await host.ConnectAsync(Seeker);
        var b = await host.ConnectAsync(Walker);
        await a.JoinAsync("lobby");
        await b.JoinAsync("lobby");

        await a.SendAsync("lobby", "hi");
        var msg = await b.Messages.NextAsync();

        Assert.Equal("Seeker", msg.User);
        Assert.Equal("u-1", msg.UserId);
        Assert.Equal("/avatars/a.png", msg.Avatar);
        Assert.DoesNotContain("RealName", msg.User);
        Assert.DoesNotContain("example", msg.User);
    }

    [Fact(DisplayName = "입장 후 새로 들어온 사람은 이전 이력을 받는다 (저장소 연결)")]
    public async Task Join_returns_history()
    {
        var store = new MemoryChatStore();
        await using var host = await ChatTestHost.StartAsync(services: ChatTestHost.Use(store: store));
        var a = await host.ConnectAsync(Seeker);
        await a.JoinAsync("lobby");
        await a.SendAsync("lobby", "하나");
        await a.SendAsync("lobby", "둘");

        var b = await host.ConnectAsync(Walker);
        var history = await b.JoinAsync("lobby");

        Assert.Equal(new[] { "하나", "둘" }, history.Select(m => m.Text).ToArray());
        Assert.Equal(2, store.Saved.Count);
        Assert.All(store.Saved, m => Assert.Equal("u-1", m.UserId));
    }

    // ── 입장과 퇴장 알림 (ChatParticipant) ─────────────────────

    [Fact(DisplayName = "입장 알림에 ID, 이름, 아바타가 담긴다")]
    public async Task UserJoined_carries_participant()
    {
        await using var host = await ChatTestHost.StartAsync();
        var a = await host.ConnectAsync(Walker);
        await a.JoinAsync("lobby");
        await a.Joined.NextAsync(); // 내 입장 알림은 비운다

        var b = await host.ConnectAsync(Seeker);
        await b.JoinAsync("lobby");

        var (room, user) = await a.Joined.NextAsync();
        Assert.Equal("lobby", room);
        Assert.Equal("u-1", user.UserId);
        Assert.Equal("Seeker", user.Name);
        Assert.Equal("/avatars/a.png", user.Avatar);
    }

    [Fact(DisplayName = "방을 나가면 퇴장 알림이 간다")]
    public async Task Leave_announces_user_left()
    {
        await using var host = await ChatTestHost.StartAsync();
        var a = await host.ConnectAsync(Walker);
        var b = await host.ConnectAsync(Seeker);
        await a.JoinAsync("lobby");
        await b.JoinAsync("lobby");

        await b.LeaveAsync("lobby");

        var (room, user) = await a.Left.NextAsync();
        Assert.Equal("lobby", room);
        Assert.Equal("Seeker", user.Name);
        Assert.Equal("u-1", user.UserId);
    }

    [Fact(DisplayName = "연결이 끊겨도 퇴장 알림이 간다")]
    public async Task Disconnect_announces_user_left()
    {
        await using var host = await ChatTestHost.StartAsync();
        var a = await host.ConnectAsync(Walker);
        var b = await host.ConnectAsync(Seeker);
        await a.JoinAsync("lobby");
        await b.JoinAsync("lobby");

        var ct = TestContext.Current.CancellationToken;
        await b.Connection.StopAsync(ct);

        var (_, user) = await a.Left.NextAsync();
        Assert.Equal("Seeker", user.Name);
    }

    [Fact(DisplayName = "입장 알림을 끄면 아무 알림도 가지 않는다")]
    public async Task AnnouncePresence_off_sends_nothing()
    {
        await using var host = await ChatTestHost.StartAsync(o => o.AnnouncePresence = false);
        var a = await host.ConnectAsync(Walker);
        await a.JoinAsync("lobby");

        var b = await host.ConnectAsync(Seeker);
        await b.JoinAsync("lobby");

        await a.Joined.AssertNoneAsync();
    }

    // ── 손님, 필명 없음 ──────────────────────────────────────

    [Fact(DisplayName = "로그인하지 않은 손님은 guest-xxxx 이고 ID 와 아바타가 없다")]
    public async Task Guest_identity()
    {
        await using var host = await ChatTestHost.StartAsync();
        var guest = await host.ConnectAsync();
        var member = await host.ConnectAsync(Walker);
        await member.JoinAsync("lobby");
        await guest.JoinAsync("lobby");
        await member.Joined.NextAsync(); // 내 입장 알림
        var (_, who) = await member.Joined.NextAsync();

        Assert.StartsWith("guest-", who.Name);
        Assert.Null(who.UserId);
        Assert.Null(who.Avatar);
    }

    [Fact(DisplayName = "필명이 없는 회원은 user-xxxx 로 보이고 실명이 새지 않는다")]
    public async Task Member_without_pseudonym_is_user_xxxx()
    {
        await using var host = await ChatTestHost.StartAsync();
        var a = await host.ConnectAsync("sub=abcd-9999;name=RealName");
        var b = await host.ConnectAsync(Walker);
        await a.JoinAsync("lobby");
        await b.JoinAsync("lobby");

        await a.SendAsync("lobby", "hi");

        var msg = await b.Messages.NextAsync();
        Assert.Equal("user-abcd", msg.User);
        Assert.Equal("abcd-9999", msg.UserId);
    }

    // ── 사이트 분리 ─────────────────────────────────────────

    [Fact(DisplayName = "허용되지 않은 사이트의 연결은 거절된다")]
    public async Task Unknown_site_is_rejected()
    {
        await using var host = await ChatTestHost.StartAsync();
        var client = host.CreateClient(query: "?site=other");

        var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        client.Connection.Closed += _ =>
        {
            closed.TrySetResult();
            return Task.CompletedTask;
        };

        try
        {
            await client.StartAsync();
        }
        catch
        {
            return; // 시작 단계에서 바로 거절됨
        }

        var ct = TestContext.Current.CancellationToken;
        await closed.Task.WaitAsync(TimeSpan.FromSeconds(5), ct);
    }

    [Fact(DisplayName = "다른 사이트의 같은 이름 방과 섞이지 않는다")]
    public async Task Sites_are_isolated()
    {
        await using var host = await ChatTestHost.StartAsync(o => o.AllowedSites = ["extra"]);
        var a = await host.ConnectAsync(Seeker);                         // 사이트 test
        var b = await host.ConnectAsync(Walker, query: "?site=extra");   // 사이트 extra
        await a.JoinAsync("lobby");
        await b.JoinAsync("lobby");

        await a.SendAsync("lobby", "여기는 test");

        await b.Messages.AssertNoneAsync();
    }

    // ── 거절과 검증 ─────────────────────────────────────────

    [Fact(DisplayName = "입장하지 않고 보내면 거절된다")]
    public async Task Send_before_join_is_rejected()
    {
        await using var host = await ChatTestHost.StartAsync();
        var a = await host.ConnectAsync(Seeker);

        var ex = await Assert.ThrowsAsync<HubException>(() => a.SendAsync("lobby", "hi"));

        Assert.Contains("먼저 방에 입장해야 합니다.", ex.Message);
    }

    [Fact(DisplayName = "잘못된 방 이름은 거절된다")]
    public async Task Invalid_room_name_is_rejected()
    {
        await using var host = await ChatTestHost.StartAsync();
        var a = await host.ConnectAsync(Seeker);

        var ex = await Assert.ThrowsAsync<HubException>(() => a.JoinAsync("bad room!"));

        Assert.Contains("방 이름이 올바르지 않습니다.", ex.Message);
    }

    [Theory(DisplayName = "예약된 방 이름(ai-)은 입장, 전송, 퇴장 모두 거절된다")]
    [InlineData("ai-news")]
    [InlineData("AI-news")]
    [InlineData("ai-0123456789abcdef-0123456789abcdef0123456789abcdef")]
    public async Task Reserved_room_prefix_is_rejected(string room)
    {
        await using var host = await ChatTestHost.StartAsync();
        var a = await host.ConnectAsync(Seeker);

        var join = await Assert.ThrowsAsync<HubException>(() => a.JoinAsync(room));
        var send = await Assert.ThrowsAsync<HubException>(() => a.SendAsync(room, "hi"));
        var leave = await Assert.ThrowsAsync<HubException>(() => a.LeaveAsync(room));

        Assert.Contains("예약된 방 이름입니다.", join.Message);
        Assert.Contains("예약된 방 이름입니다.", send.Message);
        Assert.Contains("예약된 방 이름입니다.", leave.Message);
    }

    [Theory(DisplayName = "ai 로 시작해도 접두어 'ai-' 가 아니면 입장된다")]
    [InlineData("airport")]
    [InlineData("ai_news")]
    [InlineData("ai")]
    public async Task Rooms_that_only_resemble_the_prefix_are_allowed(string room)
    {
        await using var host = await ChatTestHost.StartAsync();
        var a = await host.ConnectAsync(Seeker);

        await a.JoinAsync(room);
    }



    [Fact(DisplayName = "비어 있거나 너무 긴 메시지는 거절된다")]
    public async Task Message_length_is_validated()
    {
        await using var host = await ChatTestHost.StartAsync(o => o.MaxMessageLength = 5);
        var a = await host.ConnectAsync(Seeker);
        await a.JoinAsync("lobby");

        var tooLong = await Assert.ThrowsAsync<HubException>(() => a.SendAsync("lobby", "123456"));
        var empty = await Assert.ThrowsAsync<HubException>(() => a.SendAsync("lobby", "   "));

        Assert.Contains("메시지 길이가 올바르지 않습니다.", tooLong.Message);
        Assert.Contains("메시지 길이가 올바르지 않습니다.", empty.Message);
    }

    [Fact(DisplayName = "연결당 분당 전송 한도를 넘으면 거절된다")]
    public async Task Rate_limit_is_enforced()
    {
        await using var host = await ChatTestHost.StartAsync(o => o.MaxMessagesPerMinute = 2);
        var a = await host.ConnectAsync(Seeker);
        await a.JoinAsync("lobby");

        await a.SendAsync("lobby", "1");
        await a.SendAsync("lobby", "2");
        var ex = await Assert.ThrowsAsync<HubException>(() => a.SendAsync("lobby", "3"));

        Assert.Contains("너무 빠르게", ex.Message);
    }

    // ── 가드 ────────────────────────────────────────────────

    [Fact(DisplayName = "가드가 거절한 문장은 사유가 전달되고 다른 사람에게는 가지 않는다")]
    public async Task Guard_rejects_message()
    {
        var store = new MemoryChatStore();
        await using var host = await ChatTestHost.StartAsync(
            services: ChatTestHost.Use(store: store, guard: new RecordingGuard()));
        var a = await host.ConnectAsync(Seeker);
        var b = await host.ConnectAsync(Walker);
        await a.JoinAsync("lobby");
        await b.JoinAsync("lobby");

        var ex = await Assert.ThrowsAsync<HubException>(() => a.SendAsync("lobby", "이건 금지 표현"));

        Assert.Contains("금지된 표현입니다.", ex.Message);
        await b.Messages.AssertNoneAsync();
        Assert.Empty(store.Saved);
    }

    [Fact(DisplayName = "가드가 입장을 거절하면 입장할 수 없다")]
    public async Task Guard_rejects_join()
    {
        await using var host = await ChatTestHost.StartAsync(
            services: ChatTestHost.Use(guard: new RecordingGuard()));
        var a = await host.ConnectAsync(Seeker);

        var ex = await Assert.ThrowsAsync<HubException>(() => a.JoinAsync("secret"));

        Assert.Contains("입장할 수 없는 방입니다.", ex.Message);
    }

    [Fact(DisplayName = "가드는 필명, 아바타, 클레임 전체(Principal)를 받는다")]
    public async Task Guard_receives_identity_and_principal()
    {
        var guard = new RecordingGuard();
        await using var host = await ChatTestHost.StartAsync(services: ChatTestHost.Use(guard: guard));
        var a = await host.ConnectAsync(Seeker);

        await a.JoinAsync("lobby");

        Assert.NotNull(guard.LastCaller);
        var caller = guard.LastCaller.Value;
        Assert.True(caller.IsAuthenticated);
        Assert.Equal("u-1", caller.UserId);
        Assert.Equal("Seeker", caller.DisplayName);
        Assert.Equal("/avatars/a.png", caller.Avatar);
        Assert.NotNull(caller.Principal);
        Assert.True(caller.Principal!.HasClaim("role", "admin"));
    }

    // ── 저장소 오류 (2단계) ─────────────────────────────────

    [Fact(DisplayName = "저장에 실패하면 사유가 전달되고 방에는 전달되지 않는다")]
    public async Task Save_failure_is_reported_and_not_broadcast()
    {
        var store = new MemoryChatStore();
        await using var host = await ChatTestHost.StartAsync(services: ChatTestHost.Use(store: store));
        var a = await host.ConnectAsync(Seeker);
        var b = await host.ConnectAsync(Walker);
        await a.JoinAsync("lobby");
        await b.JoinAsync("lobby");
        store.FailSave = true;

        var ex = await Assert.ThrowsAsync<HubException>(() => a.SendAsync("lobby", "저장 안 됨"));

        Assert.Contains("메시지를 저장하지 못했습니다.", ex.Message);
        await b.Messages.AssertNoneAsync();
    }

    [Fact(DisplayName = "이력을 읽지 못하면 입장이 되돌려지고 입장 알림이 가지 않는다")]
    public async Task History_failure_rolls_back_join()
    {
        var store = new MemoryChatStore();
        await using var host = await ChatTestHost.StartAsync(services: ChatTestHost.Use(store: store));
        var a = await host.ConnectAsync(Walker);
        await a.JoinAsync("lobby");
        await a.Joined.NextAsync(); // 내 입장 알림은 비운다

        store.FailRead = true;
        var b = await host.ConnectAsync(Seeker);
        var ex = await Assert.ThrowsAsync<HubException>(() => b.JoinAsync("lobby"));

        Assert.Contains("대화 이력을 불러오지 못했습니다.", ex.Message);
        await a.Joined.AssertNoneAsync();

        // 되돌려졌으니 B 는 방에 없다: 보내려고 하면 입장하라고 한다
        var send = await Assert.ThrowsAsync<HubException>(() => b.SendAsync("lobby", "hi"));
        Assert.Contains("먼저 방에 입장해야 합니다.", send.Message);
    }

    [Fact(DisplayName = "이력 읽기가 다시 되면 같은 방에 정상 입장한다")]
    public async Task Join_works_again_after_history_recovers()
    {
        var store = new MemoryChatStore { FailRead = true };
        await using var host = await ChatTestHost.StartAsync(services: ChatTestHost.Use(store: store));
        var a = await host.ConnectAsync(Seeker);
        await Assert.ThrowsAsync<HubException>(() => a.JoinAsync("lobby"));

        store.FailRead = false;
        var history = await a.JoinAsync("lobby");

        Assert.Empty(history);
    }

    // ── 허브 연결 옵션 (MapVivaChat 오버로드) ───────────────────

    [Fact(DisplayName = "MapVivaChat 에 준 연결 옵션이 적용된다 (WebSocket 만 허용하면 롱폴링은 못 붙는다)")]
    public async Task MapVivaChat_applies_hub_options()
    {
        await using var host = await ChatTestHost.StartAsync(
            hubOptions: o => o.Transports = HttpTransportType.WebSockets);
        var client = host.CreateClient(transport: HttpTransportType.LongPolling);

        await Assert.ThrowsAnyAsync<Exception>(() => client.StartAsync());
    }

    [Fact(DisplayName = "연결 옵션을 주지 않으면 롱폴링으로도 붙는다")]
    public async Task MapVivaChat_default_allows_long_polling()
    {
        await using var host = await ChatTestHost.StartAsync();

        var client = await host.ConnectAsync(Seeker);

        Assert.Equal(Microsoft.AspNetCore.SignalR.Client.HubConnectionState.Connected, client.Connection.State);
    }
}
