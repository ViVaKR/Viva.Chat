namespace Viva.Chat.Contracts;

public interface IChatClient
{
  Task ReceiveMessage(ChatMessage message);
  Task UserJoined(string room, string user);
  Task UserLeft(string room, string user);
}
