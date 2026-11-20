using System.Security.Claims;
using Microsoft.Extensions.Options;

namespace Viva.Chat.Tests;

/// <summary>JWT 클레임에서 사용자 ID, 필명, 아바타를 읽는 기본 구현을 시험한다 (허브 없이).</summary>
public class IdentityResolverTests
{
    private static ClaimsChatIdentityResolver CreateResolver(Action<VivaChatOptions>? configure = null)
    {
        var options = new VivaChatOptions();
        configure?.Invoke(options);
        return new ClaimsChatIdentityResolver(Options.Create(options));
    }

    private static FakeHubCallerContext Authenticated(params (string Type, string Value)[] claims)
    {
        var identity = new ClaimsIdentity(claims.Select(c => new Claim(c.Type, c.Value)), "Test");
        return new FakeHubCallerContext(new ClaimsPrincipal(identity));
    }

    [Fact(DisplayName = "로그인하지 않으면 guest-xxxx 이고 ID 와 아바타는 없다")]
    public void Anonymous_is_guest()
    {
        var id = CreateResolver().Resolve(new FakeHubCallerContext(null));

        Assert.False(id.IsAuthenticated);
        Assert.Null(id.UserId);
        Assert.Null(id.Avatar);
        Assert.Null(id.Principal);
        Assert.Equal("guest-abcd", id.DisplayName);
    }

    [Fact(DisplayName = "sub, preferred_username, picture 를 읽는다")]
    public void Reads_viva_jwt_claims()
    {
        var id = CreateResolver().Resolve(Authenticated(
            ("sub", "user-guid-1"),
            ("preferred_username", "구도자"),
            ("picture", "/avatars/a.png")));

        Assert.True(id.IsAuthenticated);
        Assert.Equal("user-guid-1", id.UserId);
        Assert.Equal("구도자", id.DisplayName);
        Assert.Equal("/avatars/a.png", id.Avatar);
        Assert.NotNull(id.Principal);
    }

    [Fact(DisplayName = "sub 가 없으면 NameIdentifier 로 대신한다")]
    public void Falls_back_to_name_identifier()
    {
        var id = CreateResolver().Resolve(Authenticated(
            (ClaimTypes.NameIdentifier, "mapped-id"),
            ("preferred_username", "구도자")));

        Assert.Equal("mapped-id", id.UserId);
    }

    [Fact(DisplayName = "필명이 없으면 실명(name)과 이메일을 쓰지 않고 user-xxxx 로 보인다")]
    public void Never_uses_real_name_or_email()
    {
        var id = CreateResolver().Resolve(Authenticated(
            ("sub", "abcd-1234"),
            ("name", "홍길동"),
            ("email", "real@example.com")));

        Assert.Equal("user-abcd", id.DisplayName);
        Assert.DoesNotContain("홍길동", id.DisplayName);
        Assert.DoesNotContain("example", id.DisplayName);
    }

    [Fact(DisplayName = "아바타가 빈 문자열이면 null 이다")]
    public void Empty_avatar_becomes_null()
    {
        var id = CreateResolver().Resolve(Authenticated(
            ("sub", "u1"),
            ("preferred_username", "구도자"),
            ("picture", "")));

        Assert.Null(id.Avatar);
    }

    [Fact(DisplayName = "이름은 제어문자를 없애고 최대 길이로 자른다")]
    public void Name_is_cleaned_and_truncated()
    {
        var dirty = "구도자\n\t\u0007" + new string('가', 80);
        var id = CreateResolver(o => o.MaxDisplayNameLength = 50).Resolve(Authenticated(
            ("sub", "u1"),
            ("preferred_username", dirty)));

        Assert.Equal(50, id.DisplayName.Length);
        Assert.DoesNotContain('\n', id.DisplayName);
        Assert.DoesNotContain('\t', id.DisplayName);
        Assert.DoesNotContain('\u0007', id.DisplayName);
        Assert.StartsWith("구도자가", id.DisplayName);
    }

    [Fact(DisplayName = "이모지(서로게이트 쌍)의 가운데를 자르지 않는다")]
    public void Truncation_does_not_split_surrogate_pairs()
    {
        // 49자 + 이모지(2칸) = 51칸. 50칸에서 자르면 이모지가 반으로 갈리므로 49자만 남아야 한다.
        var name = new string('a', 49) + "😀";
        var id = CreateResolver(o => o.MaxDisplayNameLength = 50).Resolve(Authenticated(
            ("sub", "u1"),
            ("preferred_username", name)));

        Assert.Equal(new string('a', 49), id.DisplayName);
        Assert.False(char.IsHighSurrogate(id.DisplayName[^1]));
    }

    [Fact(DisplayName = "클레임 이름은 옵션으로 바꿀 수 있다")]
    public void Claim_names_are_configurable()
    {
        var id = CreateResolver(o =>
        {
            o.UserIdClaim = "uid";
            o.DisplayNameClaim = "nick";
            o.AvatarClaim = "img";
        }).Resolve(Authenticated(("uid", "u9"), ("nick", "별칭"), ("img", "/x.png")));

        Assert.Equal("u9", id.UserId);
        Assert.Equal("별칭", id.DisplayName);
        Assert.Equal("/x.png", id.Avatar);
    }
}
