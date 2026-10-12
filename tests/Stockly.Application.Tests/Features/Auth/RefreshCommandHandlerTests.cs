using NSubstitute;
using Stockly.Application.Abstractions.Identity;
using Stockly.Application.Abstractions.Messaging;
using Stockly.Application.Abstractions.Persistence;
using Stockly.Application.Features.Auth;
using Stockly.Application.Features.Auth.Refresh;
using Stockly.Application.Tests.Support;
using Stockly.Domain.Shops;
using Stockly.Domain.Users;
using static Stockly.Application.Tests.Support.AuthTestContext;

namespace Stockly.Application.Tests.Features.Auth;

public sealed class RefreshCommandHandlerTests
{
    private readonly AuthTestContext _context = new();
    private readonly RefreshToken _current = StoredToken();

    public RefreshCommandHandlerTests()
    {
        _context.RefreshTokens.FindByHashForUpdateAsync("hash-of-presented", Arg.Any<CancellationToken>()).Returns(_current);
        _context.Users.FindByIdAsync(UserId, Arg.Any<CancellationToken>()).Returns(User);
        _context.Memberships.FindFirstActiveAsync(UserId, Arg.Any<CancellationToken>()).Returns(Membership);
    }

    [Fact]
    public async Task Handle_ActiveToken_RotatesWithinTheFamily()
    {
        RefreshToken? successor = null;
        _context.RefreshTokens.Add(Arg.Do<RefreshToken>(added => successor = added));

        var result = await Handler().Handle(new RefreshCommand("presented"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.RefreshToken.Should().Be("refresh-1");
        _current.RevokedAt.Should().Be(Now);
        _current.ReplacedById.Should().Be(successor!.Id);
        successor.FamilyId.Should().Be(_current.FamilyId);
        await _context.UnitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        await _context.RefreshTokens.DidNotReceiveWithAnyArgs().RevokeFamilyAsync(default, default, default);
    }

    [Fact]
    public async Task Handle_UnknownToken_ReturnsInvalidRefreshToken()
    {
        var result = await Handler().Handle(new RefreshCommand("unknown"), CancellationToken.None);

        result.Error.Should().Be(AuthErrors.InvalidRefreshToken);
    }

    [Fact]
    public async Task Handle_RevokedToken_RevokesTheWholeFamilyAndReturnsReused()
    {
        _current.Rotate("hash-next", Now.AddHours(-1), TokenLifetimes.RefreshToken);

        var result = await Handler().Handle(new RefreshCommand("presented"), CancellationToken.None);

        result.Error.Should().Be(AuthErrors.RefreshTokenReused);
        await _context.RefreshTokens.Received(1).RevokeFamilyAsync(_current.FamilyId, Now, Arg.Any<CancellationToken>());
        _context.RefreshTokens.DidNotReceiveWithAnyArgs().Add(default!);
    }

    [Fact]
    public void Command_CommitsOnFailure_SoTheFamilyRevocationIsKept()
    {
        new RefreshCommand("x").Should().BeAssignableTo<ICommitOnFailure>();
    }

    [Fact]
    public async Task Handle_ExpiredToken_ReturnsInvalidRefreshTokenWithoutRevoking()
    {
        var expired = StoredToken(createdAt: Now - TokenLifetimes.RefreshToken);
        _context.RefreshTokens.FindByHashForUpdateAsync("hash-of-presented", Arg.Any<CancellationToken>()).Returns(expired);

        var result = await Handler().Handle(new RefreshCommand("presented"), CancellationToken.None);

        result.Error.Should().Be(AuthErrors.InvalidRefreshToken);
        await _context.RefreshTokens.DidNotReceiveWithAnyArgs().RevokeFamilyAsync(default, default, default);
    }

    [Fact]
    public async Task Handle_UserOrMembershipGone_ReturnsInvalidRefreshToken()
    {
        _context.Memberships.FindFirstActiveAsync(UserId, Arg.Any<CancellationToken>()).Returns((ShopMembership?)null);

        var result = await Handler().Handle(new RefreshCommand("presented"), CancellationToken.None);

        result.Error.Should().Be(AuthErrors.InvalidRefreshToken);
        _current.IsRevoked.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_SuspendedShop_ReturnsSuspendedWithoutRotating()
    {
        _context.Memberships.FindFirstActiveAsync(UserId, Arg.Any<CancellationToken>())
            .Returns(Membership with { ShopStatus = ShopStatus.Suspended });

        var result = await Handler().Handle(new RefreshCommand("presented"), CancellationToken.None);

        result.Error.Should().Be(ShopErrors.Suspended);
        _current.IsRevoked.Should().BeFalse();
    }

    private RefreshCommandHandler Handler() => new(
        _context.RefreshTokens, _context.Users, _context.Memberships, _context.Tokens, _context.Sessions, _context.UnitOfWork, _context.Clock);
}
