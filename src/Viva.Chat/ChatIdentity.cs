using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;
using Viva.Chat.Contracts;

namespace Viva.Chat;

/// <summary>연결 하나의 "누구인가". 연결할 때 한 번 만들어 연결 상태에 보관합니다 (JWT 스냅샷).</summary>
public sealed record ChatIdentity(
  string? UserId,
  string DisplayName,
  string? Avatar,
  bool IsAuthenticated,
  ClaimsPrincipal? Principal)
{
    public ChatParticipant ToParticipant() => new(UserId, DisplayName, Avatar);

    public ChatCaller ToCaller() => new(UserId, DisplayName, IsAuthenticated, Avatar, Principal);
}

/// <summary>
/// 연결에서 사용자 ID, 표시 이름, 아바타를 뽑는 확장점. 호스트의 인증 방식이 다르면 UseIdentityResolver 로 교체하세요.
/// 기본 구현은 Viva 규약의 JWT(sub / preferred_username / picture)를 읽습니다.
/// 이름과 아바타는 연결 시점의 토큰 값이라서, 바꾸면 다시 연결한 뒤에 반영됩니다.
/// </summary>
public interface IChatIdentityResolver
{
    ChatIdentity Resolve(HubCallerContext context);
}

public sealed class ClaimsChatIdentityResolver(IOptions<VivaChatOptions> options) : IChatIdentityResolver
{
    private const int MaxUserIdLength = 128;
    private const int MaxAvatarLength = 500;
    private readonly VivaChatOptions _opt = options.Value;

    public ChatIdentity Resolve(HubCallerContext context)
    {
        var user = context.User;
        var connectionId = context.ConnectionId;

        if (user?.Identity?.IsAuthenticated != true)
            return new ChatIdentity(null, $"guest-{Short(connectionId)}", null, false, null);

        // JwtBearer 의 클레임 매핑 설정에 따라 sub 가 NameIdentifier 로 바뀔 수 있어서 둘 다 본다.
        var userId = Clean(
          user.FindFirst(_opt.UserIdClaim)?.Value ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value,
          MaxUserIdLength);

        // 표시 이름은 필명(preferred_username)만 쓴다. name(실명)이나 email 로 대신하지 않는다.
        // 필명이 없으면 user-xxxx 로 보여서, 실명과 이메일이 채팅에 새지 않게 한다.
        var name = Clean(user.FindFirst(_opt.DisplayNameClaim)?.Value, _opt.MaxDisplayNameLength)
                   ?? $"user-{Short(userId ?? connectionId)}";

        var avatar = Clean(user.FindFirst(_opt.AvatarClaim)?.Value, MaxAvatarLength);

        return new ChatIdentity(userId, name, avatar, true, user);
    }

    private static string Short(string value) => value[..Math.Min(4, value.Length)];

    /// <summary>제어문자를 없애고 공백을 다듬어 최대 길이로 자른다. 비면 null.</summary>
    private static string? Clean(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;

        var sb = new StringBuilder(value.Length);
        foreach (var c in value)
            if (!char.IsControl(c)) sb.Append(c);

        var text = sb.ToString().Trim();
        if (text.Length == 0) return null;

        if (text.Length > maxLength)
        {
            // 이모지 같은 서로게이트 쌍의 가운데를 자르지 않는다.
            var cut = char.IsHighSurrogate(text[maxLength - 1]) ? maxLength - 1 : maxLength;
            text = text[..cut].TrimEnd();
        }

        return text.Length == 0 ? null : text;
    }
}
