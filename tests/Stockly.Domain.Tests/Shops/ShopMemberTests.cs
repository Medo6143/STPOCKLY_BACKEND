using Stockly.Domain.Shops;
using static Stockly.Domain.Tests.TestData;

namespace Stockly.Domain.Tests.Shops;

public sealed class ShopMemberTests
{
    [Theory]
    [InlineData(ShopRole.Owner)]
    [InlineData(ShopRole.Staff)]
    public void Create_ValidInput_ReturnsActiveMembership(ShopRole role)
    {
        var member = ShopMember.Create(ShopId, UserId, role, Now).Value;

        member.ShopId.Should().Be(ShopId);
        member.UserId.Should().Be(UserId);
        member.Role.Should().Be(role);
        member.IsActive.Should().BeTrue();
        member.JoinedAt.Should().Be(Now);
    }

    [Fact]
    public void Create_EmptyShop_ReturnsShopRequired()
    {
        ShopMember.Create(Guid.Empty, UserId, ShopRole.Owner, Now).Error.Should().Be(ShopMemberErrors.ShopRequired);
    }

    [Fact]
    public void Create_EmptyUser_ReturnsUserRequired()
    {
        ShopMember.Create(ShopId, Guid.Empty, ShopRole.Owner, Now).Error.Should().Be(ShopMemberErrors.UserRequired);
    }

    [Fact]
    public void Create_UndefinedRole_ReturnsUnknownRole()
    {
        ShopMember.Create(ShopId, UserId, (ShopRole)0, Now).Error.Should().Be(ShopMemberErrors.UnknownRole);
    }

    [Fact]
    public void ShopRole_Values_AreExplicitAndStable()
    {
        ((int)ShopRole.Owner).Should().Be(1);
        ((int)ShopRole.Staff).Should().Be(2);
    }
}
