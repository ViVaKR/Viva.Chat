using Viva.Chat.Contracts;

namespace Viva.Chat;

/// <summary>기본 가드: 모두 허용. 실제 규칙은 IChatGuard 구현을 UseGuard 로 교체하세요.</summary>
public sealed class AllowAllChatGuard : IChatGuard
{
  public ValueTask<bool> CanJoinAsync(ChatCaller caller, string siteId, string room, CancellationToken ct = default)
    => ValueTask.FromResult(true);

  public ValueTask<ChatVerdict> InspectAsync(ChatCaller caller, string siteId, string room, string text, CancellationToken ct = default)
    => ValueTask.FromResult(ChatVerdict.Allow());
}
