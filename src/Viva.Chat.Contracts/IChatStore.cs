namespace Viva.Chat.Contracts;

public interface IChatStore
{
  Task SaveAsync(ChatMessage message, CancellationToken ct = default);
  Task<IReadOnlyList<ChatMessage>> GetRecentAsync(
    string siteId, string room, int take, CancellationToken ct = default
  );
}
