using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.SignalR;

namespace Viva.Chat.Ai;

/// <summary>
/// AI 세션의 방 이름 규칙. 방 이름에 "사용자 해시" 를 넣어서, 다른 사용자의 세션에는 아예 접근할 수 없게 한다.
/// (별도의 세션 소유권 저장소 없이 IChatStore 만으로 동작하게 하려는 선택)
/// </summary>
internal static class AiRooms
{
    /// <summary>세션 ID 는 서버가 발급한다 (추측할 수 없는 128비트 난수).</summary>
    public static string NewSessionId() => Guid.NewGuid().ToString("N");

    public static string RequireSessionId(string? raw)
        => Guid.TryParseExact(raw, "N", out var id)
            ? id.ToString("N")
            : throw new HubException("세션 ID 가 올바르지 않습니다.");

    /// <summary>"ai-" + 사용자 해시 16자 + "-" + 세션 ID 32자 = 52자 (방 이름 한도 64자 이내)</summary>
    public static string RoomName(string userId, string sessionId)
    {
        var key = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(userId)))[..16];
        return $"{AiChatDefaults.RoomPrefix}{key}-{sessionId}";
    }

    public static string GroupName(string site, string room) => $"{site}:{room}";
}
