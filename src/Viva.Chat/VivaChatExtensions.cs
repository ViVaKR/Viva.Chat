using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Viva.Chat.Contracts;

namespace Viva.Chat;

public static class VivaChatExtensions
{
    public const string CorsPolicyName = "VivaChat";

    public static IServiceCollection AddVivaChat(
            this IServiceCollection services,
            Action<VivaChatOptions>? configure = null
    )
    {
        var options = new VivaChatOptions();
        configure?.Invoke(options);

        services.Configure<VivaChatOptions>(o => configure?.Invoke(o));
        services.AddSignalR();
        services.TryAddSingleton<IChatStore, NullChatStore>();
        if (options.AllowedOrigins.Length > 0)
        {
            services.AddCors(c => c.AddPolicy(CorsPolicyName, p => p
                .WithOrigins(options.AllowedOrigins)
                .AllowAnyHeader()
                .AllowAnyMethod()
                .AllowCredentials()
            )); // SignalR 은 AllowAnyOrigin 과 같이 못 씀
        }
        return services;
    }

    public static HubEndpointConventionBuilder MapVivaChat(this IEndpointRouteBuilder endpoints)
    {
        var options = endpoints.ServiceProvider
            .GetRequiredService<IOptions<VivaChatOptions>>().Value;

        var hub = endpoints.MapHub<ChatHub>(options.HubPath);

        if (options.AllowedOrigins.Length > 0)
            hub.RequireCors(CorsPolicyName);

        return hub;
    }
}
