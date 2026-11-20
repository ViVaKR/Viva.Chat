using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;

namespace Viva.Chat;

/// <summary>
/// 연결이 어느 "사이트"(방 이름 공간)에 속하는지 정하는 확장점. null 이면 연결을 거절합니다.
/// 기본 구현은 쿼리스트링(?site=) 이고, 이는 클라이언트가 정하므로 보안 경계가 아닙니다.
/// 멀티테넌트(예: {slug}.vivakr.com)라면 Host 나 JWT 클레임으로 서버가 정하는 구현을 UseSiteResolver 로 교체하세요.
/// </summary>
public interface ISiteResolver
{
  string? Resolve(HubCallerContext context);
}

public sealed class QuerySiteResolver(IOptions<VivaChatOptions> options) : ISiteResolver
{
  private readonly VivaChatOptions _opt = options.Value;

  public string? Resolve(HubCallerContext context)
  {
    var requested = context.GetHttpContext()?.Request.Query["site"].ToString();
    var site = string.IsNullOrWhiteSpace(requested)
      ? _opt.DefaultSiteId
      : requested.Trim().ToLowerInvariant();

    var allowed = site.Equals(_opt.DefaultSiteId, StringComparison.OrdinalIgnoreCase)
      || _opt.AllowedSites.Contains(site, StringComparer.OrdinalIgnoreCase);

    return allowed ? site : null;
  }
}
