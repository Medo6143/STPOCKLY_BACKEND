using Stockly.Domain.Users;
using static Stockly.Domain.Tests.TestData;

namespace Stockly.Domain.Tests.Users;

public sealed class RefreshTokenTests
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromDays(30);

    [Fact]
    public void StartFamily_ValidInput_FamilyIsItsOwnId()
    {
        var token = RefreshToken.StartFamily(UserId, "hash-1", " pixel-7 ", Now, Lifetime);

        token.Id.Should().NotBe(Guid.Empty);
        token.FamilyId.Should().Be(token.Id);
        token.UserId.Should().Be(UserId);
        token.TokenHash.Should().Be("hash-1");
        token.DeviceId.Should().Be("pixel-7");
        token.CreatedAt.Should().Be(Now);
        token.ExpiresAt.Should().Be(Now + Lifetime);
        token.IsRevoked.Should().BeFalse();
        token.ReplacedById.Should().BeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("  ")]
    public void StartFamily_BlankDevice_StoresNull(string? deviceId)
    {
        RefreshToken.StartFamily(UserId, "hash", deviceId, Now, Lifetime).DeviceId.Should().BeNull();
    }

    [Fact]
    public void StartFamily_EmptyUser_Throws()
    {
        var act = () => RefreshToken.StartFamily(Guid.Empty, "hash", null, Now, Lifetime);

        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void StartFamily_BlankHash_Throws(string hash)
    {
        var act = () => RefreshToken.StartFamily(UserId, hash, null, Now, Lifetime);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Rotate_ActiveToken_RevokesItAndChainsSuccessorInSameFamily()
    {
        var first = RefreshToken.StartFamily(UserId, "hash-1", "pixel-7", Now, Lifetime);
        var later = Now.AddMinutes(20);

        var second = first.Rotate("hash-2", later, Lifetime);

        first.IsRevoked.Should().BeTrue();
        first.RevokedAt.Should().Be(later);
        first.ReplacedById.Should().Be(second.Id);
        second.Id.Should().NotBe(first.Id);
        second.FamilyId.Should().Be(first.FamilyId);
        second.UserId.Should().Be(UserId);
        second.DeviceId.Should().Be("pixel-7");
        second.TokenHash.Should().Be("hash-2");
        second.CreatedAt.Should().Be(later);
        second.ExpiresAt.Should().Be(later + Lifetime);
        second.IsRevoked.Should().BeFalse();
    }

    [Fact]
    public void Rotate_TwiceDownTheChain_KeepsTheFirstTokensFamily()
    {
        var first = RefreshToken.StartFamily(UserId, "hash-1", null, Now, Lifetime);

        var third = first.Rotate("hash-2", Now, Lifetime).Rotate("hash-3", Now, Lifetime);

        third.FamilyId.Should().Be(first.Id);
    }

    [Fact]
    public void Rotate_RevokedToken_Throws()
    {
        var first = RefreshToken.StartFamily(UserId, "hash-1", null, Now, Lifetime);
        first.Rotate("hash-2", Now, Lifetime);

        var act = () => first.Rotate("hash-3", Now, Lifetime);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void IsExpired_AtAndAfterExpiry_True_Before_False()
    {
        var token = RefreshToken.StartFamily(UserId, "hash", null, Now, Lifetime);

        token.IsExpired(Now + Lifetime - TimeSpan.FromTicks(1)).Should().BeFalse();
        token.IsExpired(Now + Lifetime).Should().BeTrue();
        token.IsExpired(Now + Lifetime + TimeSpan.FromDays(1)).Should().BeTrue();
    }

    [Fact]
    public void Enums_Values_AreExplicitAndStable()
    {
        ((int)PreferredLanguage.Ar).Should().Be(1);
        ((int)PreferredLanguage.En).Should().Be(2);
        ((int)PlatformRole.None).Should().Be(0);
        ((int)PlatformRole.PlatformAdmin).Should().Be(1);
    }
}
