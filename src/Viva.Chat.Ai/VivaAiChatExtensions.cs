using AiChatServer.Contracts.V1;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Viva.Chat.Ai;

public static class VivaAiChatExtensions
{
    /// <summary>
    /// AddVivaChat 이 돌려준 빌더에 AI 채팅을 붙인다. gRPC 클라이언트 등록까지 여기서 끝난다.
    /// <code>
    /// builder.Services.AddVivaChat(o => o.DefaultSiteId = "buddham")
    ///     .UseStore&lt;PgChatStore&gt;()
    ///     .AddAiChat(o => o.ServerUrl = "http://aichat-svc.ai.svc.cluster.local:50051");
    /// </code>
    /// </summary>
    public static VivaChatBuilder AddAiChat(this VivaChatBuilder builder, Action<VivaAiChatOptions>? configure = null)
    {
        var services = builder.Services;

        // 코드 설정 → appsettings("VivaAiChat") 순으로 적용되고, 잘못되면 시작 단계에서 바로 실패한다.
        var optionsBuilder = services.AddOptions<VivaAiChatOptions>();
        if (configure is not null) optionsBuilder.Configure(configure);

        optionsBuilder
            .BindConfiguration(VivaAiChatOptions.SectionName)
            .ValidateDataAnnotations()
            .Validate(o => o.DefaultPersona != Persona.Unspecified, "VivaAiChat:DefaultPersona 를 지정하세요.")
            .ValidateOnStart();

        services.AddGrpcClient<ChatService.ChatServiceClient>((sp, client) =>
            client.Address = new Uri(sp.GetRequiredService<IOptions<VivaAiChatOptions>>().Value.ServerUrl));

        // 게이트웨이는 Scoped: 턴마다 새 gRPC 클라이언트를 받는다 (싱글턴이 클라이언트를 붙잡고 있지 않게).
        services.TryAddScoped<IAiChatGateway, GrpcAiChatGateway>();
        services.TryAddSingleton<IAiTurnGate, InMemoryAiTurnGate>();
        services.TryAddSingleton<AiChatSessionRunner>();

        return builder;
    }

    /// <summary>AI 서버 호출 방식을 바꾼다 (기본: AiChatServer gRPC).</summary>
    public static VivaChatBuilder UseAiGateway<T>(this VivaChatBuilder builder) where T : class, IAiChatGateway
    {
        builder.Services.Replace(ServiceDescriptor.Scoped<IAiChatGateway, T>());
        return builder;
    }

    /// <summary>턴 게이트를 바꾼다 (기본: 메모리. 레플리카가 여럿이면 Redis 구현으로 교체).</summary>
    public static VivaChatBuilder UseAiTurnGate<T>(this VivaChatBuilder builder) where T : class, IAiTurnGate
    {
        builder.Services.Replace(ServiceDescriptor.Singleton<IAiTurnGate, T>());
        return builder;
    }

    /// <summary>AI 채팅 허브를 매핑한다 (기본 /hubs/ai). 로그인한 사용자만 접근할 수 있다.</summary>
    public static HubEndpointConventionBuilder MapVivaAiChat(this IEndpointRouteBuilder endpoints)
    {
        var chat = endpoints.ServiceProvider.GetRequiredService<IOptions<VivaChatOptions>>().Value;
        var ai = endpoints.ServiceProvider.GetRequiredService<IOptions<VivaAiChatOptions>>().Value;

        var hub = endpoints.MapHub<AiChatHub>(ai.HubPath);

        // CORS 는 사람 채팅과 같은 규칙을 쓴다 (호스트 정책 이름이 있으면 그것, 없으면 AllowedOrigins 로 만든 자체 정책).
        var policy = !string.IsNullOrWhiteSpace(chat.CorsPolicy)
            ? chat.CorsPolicy
            : chat.AllowedOrigins.Length > 0 ? VivaChatExtensions.CorsPolicyName : null;

        if (policy is not null)
            hub.RequireCors(policy);

        // 호스트에 인증이 설정되어 있어야 한다. 없으면 시작할 때 바로 실패해서 드러난다.
        hub.RequireAuthorization();

        return hub;
    }
}
