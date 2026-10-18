using Viva.Chat.Contracts;

namespace Viva.Chat;

public class NullChatStore : IChatStore
{
  public Task<IReadOnlyList<ChatMessage>> GetRecentAsync(string siteId, string room, int take, CancellationToken ct = default)
    => Task.FromResult<IReadOnlyList<ChatMessage>>([]);

  public Task SaveAsync(ChatMessage message, CancellationToken ct = default)
    => Task.CompletedTask;
}
