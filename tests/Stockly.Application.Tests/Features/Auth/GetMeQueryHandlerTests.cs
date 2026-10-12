using NSubstitute;
using Stockly.Application.Abstractions.Identity;
using Stockly.Application.Features.Auth;
using Stockly.Application.Features.Auth.GetMe;
using Stockly.Application.Tests.Support;
using Stockly.Contracts.Auth;
using Stockly.Domain.Shops;
using static Stockly.Application.Tests.Support.AuthTestContext;
using ContractShopRole = Stockly.Contracts.Shops.ShopRole;
using ContractShopStatus = Stockly.Contracts.Shops.ShopStatus;
using ContractShopType = Stockly.Contracts.Shops.ShopType;

namespace Stockly.Application.Tests.Features.Auth;

public sealed class GetMeQueryHandlerTests
{
    private readonly AuthTestContext _context = new();

    public GetMeQueryHandlerTests()
    {
        _context.CurrentUser.UserId.Returns(UserId);
        _context.CurrentTenant.ShopId.Returns(ShopId);
        _context.Users.FindByIdAsync(UserId, Arg.Any<CancellationToken>()).Returns(User);
        _context.Memberships.FindActiveAsync(UserId, ShopId, Arg.Any<CancellationToken>()).Returns(Membership);
    }

    [Fact]
    public async Task Handle_ValidSession_ReturnsUserAndTokensShop()
    {
        var result = await Handler().Handle(new GetMeQuery(), CancellationToken.None);

        result.Value.Should().Be(new MeResponse(
            new UserDto(UserId, "mona@example.com", "Mona Adel", "0100", "ar"),
            new ShopMembershipDto(ShopId, "صيدلية الشفاء", ContractShopType.Pharmacy, ContractShopStatus.Active, ContractShopRole.Owner)));
    }

    [Fact]
    public async Task Handle_NoUserOrNoTenant_ReturnsUnauthenticated()
    {
        _context.CurrentTenant.ShopId.Returns((Guid?)null);

        var result = await Handler().Handle(new GetMeQuery(), CancellationToken.None);

        result.Error.Should().Be(AuthErrors.Unauthenticated);
    }

    [Fact]
    public async Task Handle_UserDeleted_ReturnsUnauthenticated()
    {
        _context.Users.FindByIdAsync(UserId, Arg.Any<CancellationToken>()).Returns((UserAccount?)null);

        var result = await Handler().Handle(new GetMeQuery(), CancellationToken.None);

        result.Error.Should().Be(AuthErrors.Unauthenticated);
    }

    [Fact]
    public async Task Handle_TokenForAShopTheUserIsNotMemberOf_ReturnsUnauthenticated()
    {
        var otherShop = Guid.CreateVersion7();
        _context.CurrentTenant.ShopId.Returns(otherShop);

        var result = await Handler().Handle(new GetMeQuery(), CancellationToken.None);

        result.Error.Should().Be(AuthErrors.Unauthenticated);
        await _context.Memberships.Received(1).FindActiveAsync(UserId, otherShop, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_SuspendedShop_ReturnsSuspended()
    {
        _context.Memberships.FindActiveAsync(UserId, ShopId, Arg.Any<CancellationToken>())
            .Returns(Membership with { ShopStatus = ShopStatus.Suspended });

        var result = await Handler().Handle(new GetMeQuery(), CancellationToken.None);

        result.Error.Should().Be(ShopErrors.Suspended);
    }

    [Fact]
    public async Task Handle_MembershipLookup_IsAlwaysForTheCurrentUser()
    {
        await Handler().Handle(new GetMeQuery(), CancellationToken.None);

        await _context.Memberships.Received(1).FindActiveAsync(UserId, ShopId, Arg.Any<CancellationToken>());
        await _context.Memberships.DidNotReceive().FindActiveAsync(Arg.Is<Guid>(id => id != UserId), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    private GetMeQueryHandler Handler() => new(_context.CurrentUser, _context.CurrentTenant, _context.Users, _context.Memberships);
}
