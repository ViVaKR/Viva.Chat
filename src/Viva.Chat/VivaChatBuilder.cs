using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Viva.Chat.Contracts;

namespace Viva.Chat;

/// <summary>
/// AddVivaChat 이 돌려주는 빌더. 확장점 교체와 SignalR 설정(Redis 백플레인 등)을 이어서 쓸 수 있다.
/// <code>
/// builder.Services.AddVivaChat(o => o.DefaultSiteId = "buddham")
///     .UseStore&lt;PgChatStore&gt;()
///     .UseGuard&lt;BuddhamChatGuard&gt;()
///     .SignalR.AddStackExchangeRedis(redisConnection, o => o.Configuration.ChannelPrefix = RedisChannel.Literal("buddham:signalr"));
/// </code>
/// </summary>
public sealed class VivaChatBuilder(IServiceCollection services, ISignalRServerBuilder signalR)
{
    public IServiceCollection Services { get; } = services;

    public ISignalRServerBuilder SignalR { get; } = signalR;

    public VivaChatBuilder UseStore<T>() where T : class, IChatStore
    {
        Services.Replace(ServiceDescriptor.Scoped<IChatStore, T>());
        return this;
    }

    public VivaChatBuilder UseGuard<T>() where T : class, IChatGuard
    {
        Services.Replace(ServiceDescriptor.Scoped<IChatGuard, T>());
        return this;
    }

    public VivaChatBuilder UseSiteResolver<T>() where T : class, ISiteResolver
    {
        Services.Replace(ServiceDescriptor.Scoped<ISiteResolver, T>());
        return this;
    }

    /// <summary>사용자 ID, 표시 이름, 아바타를 뽑는 방식을 바꾼다 (기본: Viva 규약 JWT 클레임).</summary>
    public VivaChatBuilder UseIdentityResolver<T>() where T : class, IChatIdentityResolver
    {
        Services.Replace(ServiceDescriptor.Scoped<IChatIdentityResolver, T>());
        return this;
    }
}
