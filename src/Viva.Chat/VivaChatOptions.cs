using System.ComponentModel.DataAnnotations;

namespace Viva.Chat;

/// <summary>
/// 설정 옵션. 0.2 부터 Contracts 가 아니라 이 프로젝트(ViVaKR.Chat)에 있고,
/// 코드 설정 뒤에 appsettings 의 "VivaChat" 섹션이 덮어씁니다. 시작할 때 검증됩니다.
/// </summary>
public class VivaChatOptions
{
  public const string SectionName = "VivaChat";

  /// <summary>접속 시 site 쿼리가 없을 때 쓰는 사이트 ID (항상 허용)</summary>
  [Required]
  public string DefaultSiteId { get; set; } = "default";

  /// <summary>기본 리졸버(QuerySiteResolver)가 추가로 받아들일 사이트 ID</summary>
  public string[] AllowedSites { get; set; } = [];

  /// <summary>
  /// CORS 허용 오리진. 비워 두면 CORS 정책을 등록하지 않음.
  /// 호스트에 이미 CORS 정책이 있다면 이걸 채우지 말고 <see cref="CorsPolicy"/> 를 쓰세요.
  /// </summary>
  public string[] AllowedOrigins { get; set; } = [];

  /// <summary>
  /// 호스트가 이미 등록한 CORS 정책 이름 (0.3). 지정하면 허브에 이 정책을 쓰고,
  /// <see cref="AllowedOrigins"/> 로 만드는 자체 정책(VivaChat)은 만들지 않습니다.
  /// 허브에 걸리는 정책(엔드포인트)은 호스트의 전역 UseCors 보다 우선하므로, 호스트의 도메인 규칙을 그대로 쓰려면 이 옵션을 쓰세요.
  /// </summary>
  public string? CorsPolicy { get; set; }

  /// <summary>허브 경로. 규약은 /hubs/&lt;이름&gt; 입니다 (채팅 /hubs/chat, AI 채팅 /hubs/ai).</summary>
  [Required, RegularExpression("^/.*", ErrorMessage = "HubPath 는 '/' 로 시작해야 합니다.")]
  public string HubPath { get; set; } = "/hubs/chat";

  [Range(1, 8_000)]
  public int MaxMessageLength { get; set; } = 2000;

  [Range(0, 500)]
  public int HistoryOnJoin { get; set; } = 50;

  /// <summary>연결 하나당 분당 최대 메시지 수 (0 이면 제한 없음). 사용자 단위 제한은 호스트의 몫입니다.</summary>
  [Range(0, 600)]
  public int MaxMessagesPerMinute { get; set; } = 30;

  /// <summary>true 면 허브 엔드포인트에 RequireAuthorization() 을 적용 (호스트에 인증이 설정되어 있어야 함)</summary>
  public bool RequireAuthentication { get; set; }

  /// <summary>입장·퇴장 알림 전송 여부. 사람이 많은 방에서는 끄는 편이 좋습니다.</summary>
  public bool AnnouncePresence { get; set; } = true;

  // ── 사용자 식별 (0.3). 기본값은 Viva 공통 JWT 규약이라 대부분의 사이트는 설정할 필요가 없습니다. ──

  /// <summary>사용자 ID 클레임 이름. 없으면 NameIdentifier 로 대신합니다.</summary>
  [Required]
  public string UserIdClaim { get; set; } = "sub";

  /// <summary>표시 이름(필명) 클레임 이름. 실명(name)이나 이메일로 대신하지 않습니다.</summary>
  [Required]
  public string DisplayNameClaim { get; set; } = "preferred_username";

  /// <summary>아바타(이미지 경로) 클레임 이름</summary>
  [Required]
  public string AvatarClaim { get; set; } = "picture";

  /// <summary>표시 이름 최대 길이 (넘으면 잘림)</summary>
  [Range(1, 100)]
  public int MaxDisplayNameLength { get; set; } = 50;
}
