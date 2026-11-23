using Viva.Chat.Contracts;

namespace Viva.Chat.Ai;

/// <summary>서버 → 클라이언트 이벤트. 모든 이벤트는 방 이름이 아니라 sessionId 로 구분합니다.</summary>
public interface IAiChatClient
{
    /// <summary>사용자가 보낸 질문의 에코 (같은 사용자의 다른 탭에서도 보이게 함)</summary>
    Task ReceiveMessage(string sessionId, ChatMessage message);

    /// <summary>답변 조각. kind 는 <see cref="AiChunkKind"/> (thinking / content)</summary>
    Task ReceiveChunk(string sessionId, string streamId, string kind, string delta);

    /// <summary>답변 완료. message.Id 는 streamId 와 같으므로 스트리밍 중이던 말풍선을 이 메시지로 바꾸면 됩니다.</summary>
    Task StreamCompleted(string sessionId, string streamId, ChatMessage message);

    /// <summary>답변 실패. 스트리밍 중이던 말풍선은 버리고 reason 을 보여 주세요. (저장되지 않았습니다)</summary>
    Task StreamFailed(string sessionId, string streamId, string reason);

    /// <summary>사용자가 중단. 지금까지 받은 답을 저장했다면 partial 에 담겨 옵니다 (없으면 null).</summary>
    Task StreamCancelled(string sessionId, string streamId, ChatMessage? partial);
}

/// <summary><c>Open</c> 의 반환값. 클라이언트에는 { sessionId, history } 로 보입니다.</summary>
public sealed record AiSessionOpened(string SessionId, IReadOnlyList<ChatMessage> History);

/// <summary><c>Send</c> 의 반환값. 답변은 이벤트로 흘러오며, 같은 streamId 가 모든 조각에 붙습니다.</summary>
public sealed record AiTurnAccepted(string UserMessageId, string StreamId);
