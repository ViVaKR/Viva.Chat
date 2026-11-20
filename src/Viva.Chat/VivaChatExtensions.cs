using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Viva.Chat.Contracts;

namespace Viva.Chat;

public static class VivaChatExtensions
{
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

        // CORS 정책은 최종 옵션 값(코드 + appsettings)을 읽어서 만든다.
        services.AddCors();
        services.AddOptions<CorsOptions>()
            .Configure<IOptions<VivaChatOptions>>((cors, chat) =>
            {
                var origins = chat.Value.AllowedOrigins;
                if (origins.Length == 0) return;

                cors.AddPolicy(CorsPolicyName, p => p
                    .WithOrigins(origins)
                    .AllowAnyHeader()
                    .AllowAnyMethod()
                    .AllowCredentials());
            });

        return new VivaChatBuilder(services, services.AddSignalR());
    }

    public static HubEndpointConventionBuilder MapVivaChat(this IEndpointRouteBuilder endpoints)
    {
        var options = endpoints.ServiceProvider.GetRequiredService<IOptions<VivaChatOptions>>().Value;

        var hub = endpoints.MapHub<ChatHub>(options.HubPath);

        if (options.AllowedOrigins.Length > 0)
            hub.RequireCors(CorsPolicyName);

        if (options.RequireAuthentication)
            hub.RequireAuthorization();

        return hub;
    }
}
