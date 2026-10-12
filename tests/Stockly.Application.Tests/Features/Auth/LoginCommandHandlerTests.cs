using NSubstitute;
using Stockly.Application.Abstractions.Persistence;
using Stockly.Application.Features.Auth;
using Stockly.Application.Features.Auth.Login;
using Stockly.Application.Tests.Support;
using Stockly.Domain.Shops;
using Stockly.Domain.Users;
using static Stockly.Application.Tests.Support.AuthTestContext;

namespace Stockly.Application.Tests.Features.Auth;

public sealed class LoginCommandHandlerTests
{
    private readonly AuthTestContext _context = new();

    public LoginCommandHandlerTests()
    {
        _context.Users.FindByCredentialsAsync("mona@example.com", "Str0ngPass", Arg.Any<CancellationToken>()).Returns(User);
        _context.Memberships.FindFirstActiveAsync(UserId, Arg.Any<CancellationToken>()).Returns(Membership);
    }

    [Fact]
    public async Task Handle_ValidCredentials_StartsANewFamilyForTheFirstMembership()
    {
        RefreshToken? stored = null;
        _context.RefreshTokens.Add(Arg.Do<RefreshToken>(added => stored = added));

        var result = await Handler().Handle(new LoginCommand(" mona@example.com ", "Str0ngPass", "tab-1"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Shop.Id.Should().Be(ShopId);
        stored!.FamilyId.Should().Be(stored.Id, "each login starts a family");
        stored.DeviceId.Should().Be("tab-1");
        await _context.Users.Received(1).RecordSignInAsync(UserId, Now, Arg.Any<CancellationToken>());
        await _context.UnitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WrongCredentials_ReturnsInvalidCredentials()
    {
        var result = await Handler().Handle(new LoginCommand("mona@example.com", "nope", null), CancellationToken.None);

        result.Error.Should().Be(AuthErrors.InvalidCredentials);
        _context.RefreshTokens.DidNotReceiveWithAnyArgs().Add(default!);
    }

    [Fact]
    public async Task Handle_NoActiveMembership_ReturnsNoActiveShop()
    {
        _context.Memberships.FindFirstActiveAsync(UserId, Arg.Any<CancellationToken>()).Returns((ShopMembership?)null);

        var result = await Handler().Handle(new LoginCommand("mona@example.com", "Str0ngPass", null), CancellationToken.None);

        result.Error.Should().Be(AuthErrors.NoActiveShop);
    }

    [Fact]
    public async Task Handle_SuspendedShop_ReturnsSuspendedAndIssuesNothing()
    {
        _context.Memberships.FindFirstActiveAsync(UserId, Arg.Any<CancellationToken>())
            .Returns(Membership with { ShopStatus = ShopStatus.Suspended });

        var result = await Handler().Handle(new LoginCommand("mona@example.com", "Str0ngPass", null), CancellationToken.None);

        result.Error.Should().Be(ShopErrors.Suspended);
        _context.RefreshTokens.DidNotReceiveWithAnyArgs().Add(default!);
        await _context.Users.DidNotReceiveWithAnyArgs().RecordSignInAsync(default, default, default);
    }

    private LoginCommandHandler Handler() => new(_context.Users, _context.Memberships, _context.Sessions, _context.UnitOfWork, _context.Clock);
}
