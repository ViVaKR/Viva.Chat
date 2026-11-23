using System.ComponentModel.DataAnnotations;
using AiChatServer.Contracts.V1;

namespace Viva.Chat.Ai;

/// <summary>AI 채팅 옵션. 코드 설정 뒤에 appsettings 의 "VivaAiChat" 섹션이 덮어쓰고, 시작할 때 검증됩니다.</summary>
public sealed class VivaAiChatOptions
{
    public const string SectionName = "VivaAiChat";

    /// <summary>AiChatServer(gRPC) 주소. 예) http://aichat-svc.ai.svc.cluster.local:50051</summary>
    [Required, Url]
    public string ServerUrl { get; set; } = string.Empty;

    public Persona DefaultPersona { get; set; } = Persona.Beopdeung;

    /// <summary>채팅에 보이는 AI 의 이름. (AI 발화의 구분은 이름이 아니라 UserId 로 한다)</summary>
    [Required]
    public string AiDisplayName { get; set; } = "법등";

    /// <summary>허브 경로. 규약은 /hubs/&lt;이름&gt; 입니다.</summary>
    [Required, RegularExpression("^/.*", ErrorMessage = "HubPath 는 '/' 로 시작해야 합니다.")]
    public string HubPath { get; set; } = "/hubs/ai";

    /// <summary>모델의 사고 과정(thinking)을 클라이언트에 보낼지 여부. 사고 과정은 저장하지 않습니다.</summary>
    public bool EmitThinking { get; set; } = true;

    [Range(1, 8_000)]
    public int MaxPromptLength { get; set; } = 2000;

    /// <summary>AI 에게 함께 보내는 이전 대화 개수 (0 이면 이번 질문만)</summary>
    [Range(0, 100)]
    public int ContextHistoryCount { get; set; } = 20;

    /// <summary>세션을 열 때 돌려주는 이력 개수</summary>
    [Range(0, 500)]
    public int HistoryOnOpen { get; set; } = 50;

    /// <summary>연결 하나가 1분에 시작할 수 있는 턴 수 (0 이면 제한 없음). GPU 를 여러 사이트가 나눠 쓰므로 기본값을 낮게 둡니다.</summary>
    [Range(0, 600)]
    public int MaxTurnsPerMinute { get; set; } = 6;

    /// <summary>사용자가 중단했을 때 지금까지 받은 답을 저장할지 여부 (화면에 보인 내용이 이력에서 사라지지 않게 함)</summary>
    public bool SavePartialOnCancel { get; set; } = true;

    [Range(5, 1800)]
    public int RequestTimeoutSeconds { get; set; } = 300;
}
