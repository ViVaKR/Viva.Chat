namespace Viva.Chat.Contracts;

public interface IChatClient
{
  Task ReceiveMessage(ChatMessage message);

  /// <summary>0.3 부터 이름 문자열이 아니라 <see cref="ChatParticipant"/> 를 받습니다.</summary>
  Task UserJoined(string room, ChatParticipant user);

  Task UserLeft(string room, ChatParticipant user);
}
