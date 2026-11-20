namespace Viva.Chat.Contracts;

/// <summary>
/// 채팅 메시지 하나.
/// <c>UserId</c> 는 0.2 에서, <c>Avatar</c> 는 0.3 에서 추가되었습니다 (로그인하지 않았으면 둘 다 null).
/// 신고, 차단, 본인 글 삭제처럼 "사람"을 가리켜야 할 때는 표시 이름(User)이 아니라 UserId 를 쓰세요.
/// <c>Avatar</c> 는 보낸 시점의 스냅샷입니다. 나중에 프로필 사진을 바꿔도 지난 메시지는 그대로 보입니다.
/// </summary>
public sealed record ChatMessage
(
  string Id,
  string SiteId,
  string Room,
  string User,
  string Text,
  DateTimeOffset SentAt,
  string? UserId = null,
  string? Avatar = null
);
