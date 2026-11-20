using System.Security.Claims;

namespace Viva.Chat.Contracts;

/// <summary>
/// 호출한 사람. 허브가 연결 정보에서 만들어 가드에 넘깁니다.
/// <c>Principal</c> 은 로그인한 경우의 JWT 클레임 전체라서, 가드가 나이나 역할 같은 클레임을 직접 볼 수 있습니다 (손님이면 null).
/// <c>ClaimsPrincipal</c> 은 .NET 기본 라이브러리 타입이라 Contracts 의 순수성을 해치지 않습니다.
/// </summary>
public readonly record struct ChatCaller(
  string? UserId,
  string DisplayName,
  bool IsAuthenticated,
  string? Avatar = null,
  ClaimsPrincipal? Principal = null);

/// <summary>메시지 검사 결과. 허용(원문 그대로 또는 고쳐 쓴 문장) 또는 거절(사유).</summary>
public readonly record struct ChatVerdict(bool Allowed, string? Text = null, string? Reason = null)
{
  public static ChatVerdict Allow(string? rewrittenText = null) => new(true, rewrittenText);

  public static ChatVerdict Reject(string reason) => new(false, null, reason);
}

/// <summary>
/// 방 입장 권한과 메시지 내용 검사를 한 곳에서 맡는 확장점.
/// 비공개 방, 등급별 게시판, 욕설·개인정보 필터 같은 사이트별 규칙을 여기에 둡니다.
/// 기본 구현은 모두 허용합니다.
/// </summary>
public interface IChatGuard
{
  ValueTask<bool> CanJoinAsync(ChatCaller caller, string siteId, string room, CancellationToken ct = default);

  ValueTask<ChatVerdict> InspectAsync(ChatCaller caller, string siteId, string room, string text, CancellationToken ct = default);
}
