namespace Viva.Chat;

/// <summary>
/// 방 이름 규약. 다른 패키지(ViVaKR.AiChat 등)가 쓰는 방을 사람 채팅(ChatHub)에서 엿보지 못하도록 접두어를 예약합니다.
/// </summary>
public static class ChatRoomRules
{
    /// <summary>예약된 방 이름 접두어. ChatHub 는 이 접두어로 시작하는 방을 거절합니다.</summary>
    public const string ReservedPrefix = "ai-";

    public static bool IsReserved(string room)
        => room.StartsWith(ReservedPrefix, StringComparison.OrdinalIgnoreCase);
}
