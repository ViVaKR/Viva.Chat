namespace Viva.Chat.Contracts;

public class VivaChatOptions
{
  /// <summary>접속 시 site 쿼리가 없을 때 쓰는 사이트 ID</summary>
  public string DefaultSiteId { get; set; } = "default";

  /// <summary>중앙 서비스로 운영할 때 허용할 사이트 ID 목록 (DefaultSiteId는 항상 허용)</summary>
  public string[] AllowedSites { get; set; } = [];

  /// <summary>CORS 허용 오리진. 비워 두면 CORS 정책을 등록하지 않음</summary>
  public string[] AllowedOrigins { get; set; } = [];

  public string HubPath { get; set; } = "/hubs/chat";

  public int MaxMessageLength { get; set; } = 2000;

  public int HistoryOnJoin { get; set; } = 50;
}
