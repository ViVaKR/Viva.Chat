using AiChatServer.Contracts.V1;

namespace Viva.Chat.Ai;

public static class AiChatDefaults
{
    /// <summary>AI 세션 방 이름의 접두어. 사람 채팅(ChatHub)은 이 접두어의 방을 거절합니다.</summary>
    public const string RoomPrefix = ChatRoomRules.ReservedPrefix;

    /// <summary>AI 가 보낸 메시지의 UserId 접두어. 표시 이름이 아니라 이 값으로 AI 발화를 구분합니다.</summary>
    public const string AiUserIdPrefix = "ai:";

    public static string AiUserId(Persona persona)
        => AiUserIdPrefix + persona.ToString().ToLowerInvariant();

    public static bool IsAiUserId(string? userId)
        => userId is not null && userId.StartsWith(AiUserIdPrefix, StringComparison.Ordinal);
}

/// <summary><c>ReceiveChunk</c> 의 kind 값.</summary>
public static class AiChunkKind
{
    public const string Thinking = "thinking";
    public const string Content = "content";
}
