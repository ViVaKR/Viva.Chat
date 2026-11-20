using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Threading.Channels;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Http.Connections.Client;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Viva.Chat.Contracts;

namespace Viva.Chat.Tests;

/// <summary>
/// 테스트용 인증. 요청 헤더 X-Test-Claims 에 "type=value;type=value" 를 넣으면 그 클레임을 가진 사용자로 인정한다.
/// 헤더가 없으면 로그인하지 않은 손님이다. (헤더는 ASCII 만 쓸 수 있으니 값도 ASCII 로 쓴다)
/// </summary>
public sealed class TestAuthHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public new const string Scheme = "Test";
    public const string HeaderName = "X-Test-Claims";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(HeaderName, out var raw))
            return Task.FromResult(AuthenticateResult.NoResult());

        var claims = raw.ToString()
            .Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Split('=', 2))
            .Where(p => p.Length == 2)
            .Select(p => new Claim(p[0], p[1]))
            .ToList();

        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, Scheme));
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme)));
    }
}

/// <summary>저장소 대역. 메모리에 쌓고, 원하면 일부러 실패시킨다.</summary>
public sealed class MemoryChatStore : IChatStore
{
    private readonly List<ChatMessage> _items = [];

    public bool FailSave { get; set; }
    public bool FailRead { get; set; }

    public IReadOnlyList<ChatMessage> Saved
    {
        get { lock (_items) return _items.ToArray(); }
    }

    public Task SaveAsync(ChatMessage message, CancellationToken ct = default)
    {
        if (FailSave) throw new InvalidOperationException("db down");
        lock (_items) _items.Add(message);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<ChatMessage>> GetRecentAsync(string siteId, string room, int take, CancellationToken ct = default)
    {
        if (FailRead) throw new InvalidOperationException("db down");

        lock (_items)
        {
            IReadOnlyList<ChatMessage> result = _items
                .Where(m => m.SiteId == siteId && m.Room == room)
                .OrderBy(m => m.SentAt)
                .TakeLast(take)
                .ToArray();
            return Task.FromResult(result);
        }
    }
}

/// <summary>가드 대역. 마지막으로 받은 호출자를 기억하고, "금지"가 들어간 문장과 "secret" 방을 거절한다.</summary>
public sealed class RecordingGuard : IChatGuard
{
    public ChatCaller? LastCaller { get; private set; }

    public ValueTask<bool> CanJoinAsync(ChatCaller caller, string siteId, string room, CancellationToken ct = default)
    {
        LastCaller = caller;
        return ValueTask.FromResult(room != "secret");
    }

    public ValueTask<ChatVerdict> InspectAsync(ChatCaller caller, string siteId, string room, string text, CancellationToken ct = default)
    {
        LastCaller = caller;
        return ValueTask.FromResult(text.Contains("금지") ? ChatVerdict.Reject("금지된 표현입니다.") : ChatVerdict.Allow());
    }
}

/// <summary>서버가 보낸 이벤트를 차례로 받아 두었다가 테스트가 꺼내 보는 상자</summary>
public sealed class Inbox<T>
{
    private readonly Channel<T> _channel = Channel.CreateUnbounded<T>();

    public void Add(T item) => _channel.Writer.TryWrite(item);

    public async Task<T> NextAsync(int timeoutMs = 5000)
    {
        using var cts = new CancellationTokenSource(timeoutMs);
        try
        {
            return await _channel.Reader.ReadAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            throw new TimeoutException("기다리던 이벤트가 제시간에 오지 않았습니다.");
        }
    }

    /// <summary>잠시 기다려도 아무것도 오지 않아야 통과한다.</summary>
    public async Task AssertNoneAsync(int waitMs = 400)
    {
        using var cts = new CancellationTokenSource(waitMs);
        try
        {
            var item = await _channel.Reader.ReadAsync(cts.Token);
            Assert.Fail($"오지 않아야 할 이벤트가 왔습니다: {item}");
        }
        catch (OperationCanceledException)
        {
            // 아무것도 오지 않았다 = 통과
        }
    }
}

/// <summary>테스트용 클라이언트 한 명. 서버 이벤트를 각 상자에 모은다.</summary>
public sealed class TestClient : IAsyncDisposable
{
    public HubConnection Connection { get; }
    public Inbox<ChatMessage> Messages { get; } = new();
    public Inbox<(string Room, ChatParticipant User)> Joined { get; } = new();
    public Inbox<(string Room, ChatParticipant User)> Left { get; } = new();

    public TestClient(HubConnection connection)
    {
        Connection = connection;
        Connection.On<ChatMessage>("ReceiveMessage", m => Messages.Add(m));
        Connection.On<string, ChatParticipant>("UserJoined", (room, user) => Joined.Add((room, user)));
        Connection.On<string, ChatParticipant>("UserLeft", (room, user) => Left.Add((room, user)));
    }

    public Task StartAsync() => Connection.StartAsync();

    public Task<ChatMessage[]> JoinAsync(string room) => Connection.InvokeAsync<ChatMessage[]>("Join", room);

    public Task LeaveAsync(string room) => Connection.InvokeAsync("Leave", room);

    public Task SendAsync(string room, string text) => Connection.InvokeAsync("SendMessage", room, text);

    public ValueTask DisposeAsync() => Connection.DisposeAsync();
}

/// <summary>메모리 안에서 도는 테스트 서버 (실제 네트워크 포트를 열지 않는다)</summary>
public sealed class ChatTestHost : IAsyncDisposable
{
    public const string HubUrl = "/hubs/chat";

    private readonly WebApplication _app;
    private readonly TestServer _server;
    private readonly List<TestClient> _clients = [];

    private ChatTestHost(WebApplication app, TestServer server)
    {
        _app = app;
        _server = server;
    }

    public static async Task<ChatTestHost> StartAsync(
        Action<VivaChatOptions>? options = null,
        Action<IServiceCollection>? services = null,
        Action<HttpConnectionDispatcherOptions>? hubOptions = null)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();

        builder.Services
            .AddAuthentication(TestAuthHandler.Scheme)
            .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.Scheme, _ => { });
        builder.Services.AddAuthorization();

        builder.Services.AddVivaChat(o =>
        {
            o.DefaultSiteId = "test";
            options?.Invoke(o);
        });

        // AddVivaChat 뒤에 불러서, 테스트가 넣은 대역이 기본 구현을 덮어쓰게 한다.
        services?.Invoke(builder.Services);

        var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();

        if (hubOptions is null) app.MapVivaChat();
        else app.MapVivaChat(hubOptions);

        await app.StartAsync();

        return new ChatTestHost(app, (TestServer)app.Services.GetRequiredService<IServer>());
    }

    /// <summary>같은 인스턴스를 모든 요청에서 쓰도록 등록하는 도우미</summary>
    public static Action<IServiceCollection> Use(IChatStore? store = null, IChatGuard? guard = null) => s =>
    {
        if (store is not null) s.Replace(ServiceDescriptor.Singleton(store));
        if (guard is not null) s.Replace(ServiceDescriptor.Singleton(guard));
    };

    /// <summary>클라이언트를 만든다 (시작은 하지 않음). claims 가 null 이면 로그인하지 않은 손님.</summary>
    public TestClient CreateClient(string? claims = null, string query = "", HttpTransportType transport = HttpTransportType.LongPolling)
    {
        var connection = new HubConnectionBuilder()
            .WithUrl(new Uri(_server.BaseAddress, HubUrl + query), o =>
            {
                o.Transports = transport;
                o.HttpMessageHandlerFactory = _ => _server.CreateHandler();
                if (claims is not null) o.Headers[TestAuthHandler.HeaderName] = claims;
            })
            .Build();

        var client = new TestClient(connection);
        _clients.Add(client);
        return client;
    }

    /// <summary>클라이언트를 만들고 연결까지 마친다.</summary>
    public async Task<TestClient> ConnectAsync(string? claims = null, string query = "")
    {
        var client = CreateClient(claims, query);
        await client.StartAsync();
        return client;
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var c in _clients)
        {
            try { await c.DisposeAsync(); } catch { /* 정리 중 오류는 무시 */ }
        }

        await _app.StopAsync();
        await _app.DisposeAsync();
    }
}

/// <summary>연결 정보 대역. 식별 리졸버를 허브 없이 시험하려고 쓴다.</summary>
public sealed class FakeHubCallerContext(ClaimsPrincipal? user, string connectionId = "abcd1234efgh") : HubCallerContext
{
    public override string ConnectionId => connectionId;
    public override string? UserIdentifier => null;
    public override ClaimsPrincipal? User => user;
    public override IDictionary<object, object?> Items { get; } = new Dictionary<object, object?>();
    public override IFeatureCollection Features { get; } = new FeatureCollection();
    public override CancellationToken ConnectionAborted => CancellationToken.None;
    public override void Abort() { }
}
