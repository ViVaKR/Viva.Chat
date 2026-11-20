namespace Viva.Chat.Contracts;

/// <summary>
/// 방에 들어오거나 나간 사람. 0.3 에서 입장·퇴장 알림이 이름 문자열 대신 이 타입을 씁니다.
/// 로그인하지 않은 손님은 <c>UserId</c> 와 <c>Avatar</c> 가 null 이고, 이름만 <c>guest-xxxx</c> 형태로 채워집니다.
/// </summary>
public sealed record ChatParticipant(string? UserId, string Name, string? Avatar = null);
