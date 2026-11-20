using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Viva.Chat.Contracts;

namespace Viva.Chat;

public static class VivaChatExtensions
{
    /// <summary>AllowedOrigins 로 만드는 자체 CORS 정책의 이름</summary>
    public const string CorsPolicyName = "VivaChat";

    public static VivaChatBuilder AddVivaChat(this IServiceCollection services, Action<VivaChatOptions>? configure = null)
    {
        // 코드 설정 → appsettings("VivaChat") 순으로 적용되어 배포 환경 설정이 코드를 덮어쓴다.
        // 잘못된 값은 시작 단계에서 바로 실패한다 (K8s 에서 CrashLoop 로 즉시 드러남).
        var optionsBuilder = services.AddOptions<VivaChatOptions>();
        if (configure is not null) optionsBuilder.Configure(configure);

        optionsBuilder
            .BindConfiguration(VivaChatOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<IChatStore, NullChatStore>();
        services.TryAddSingleton<IChatGuard, AllowAllChatGuard>();
        services.TryAddSingleton<ISiteResolver, QuerySiteResolver>();
        services.TryAddSingleton<IChatIdentityResolver, ClaimsChatIdentityResolver>();

        // 자체 CORS 정책은 최종 옵션 값(코드 + appsettings)을 읽어서 만든다.
        // 호스트 정책을 쓰기로 했거나(CorsPolicy) 오리진이 없으면 만들지 않는다.
        services.AddCors();
        services.AddOptions<CorsOptions>()
            .Configure<IOptions<VivaChatOptions>>((cors, chat) =>
            {
                var opt = chat.Value;
                if (!string.IsNullOrWhiteSpace(opt.CorsPolicy)) return;
                if (opt.AllowedOrigins.Length == 0) return;

                cors.AddPolicy(CorsPolicyName, p => p
                    .WithOrigins(opt.AllowedOrigins)
                    .AllowAnyHeader()
                    .AllowAnyMethod()
                    .AllowCredentials());
            });

        return new VivaChatBuilder(services, services.AddSignalR());
    }

    public static HubEndpointConventionBuilder MapVivaChat(this IEndpointRouteBuilder endpoints)
        => endpoints.MapVivaChat(null);

    /// <summary>
    /// 허브를 매핑한다. <paramref name="configure"/> 로 전송 방식, 버퍼 크기, 상태 유지 재연결 같은 허브 연결 옵션을 줄 수 있다.
    /// <code>
    /// app.MapVivaChat(o =>
    /// {
    ///     o.Transports = HttpTransportType.WebSockets | HttpTransportType.ServerSentEvents;
    ///     o.AllowStatefulReconnects = true;
    /// });
    /// </code>
    /// </summary>
    public static HubEndpointConventionBuilder MapVivaChat(
        this IEndpointRouteBuilder endpoints,
        Action<HttpConnectionDispatcherOptions>? configure)
    {
        var options = endpoints.ServiceProvider.GetRequiredService<IOptions<VivaChatOptions>>().Value;

        var hub = configure is null
            ? endpoints.MapHub<ChatHub>(options.HubPath)
            : endpoints.MapHub<ChatHub>(options.HubPath, configure);

        // 호스트 정책 이름이 있으면 그걸 쓰고, 없으면 오리진 목록으로 만든 자체 정책을 쓴다.
        var policy = !string.IsNullOrWhiteSpace(options.CorsPolicy)
            ? options.CorsPolicy
            : options.AllowedOrigins.Length > 0 ? CorsPolicyName : null;

        if (policy is not null)
            hub.RequireCors(policy);

        if (options.RequireAuthentication)
            hub.RequireAuthorization();

        return hub;
    }
}
