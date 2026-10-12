using NSubstitute;
using Stockly.Application.Features.Auth;
using Stockly.Application.Features.Auth.Logout;
using Stockly.Application.Tests.Support;
using Stockly.Domain.Users;
using static Stockly.Application.Tests.Support.AuthTestContext;

namespace Stockly.Application.Tests.Features.Auth;

public sealed class LogoutCommandHandlerTests
{
    private readonly AuthTestContext _context = new();

    public LogoutCommandHandlerTests()
    {
        _context.CurrentUser.UserId.Returns(UserId);
    }

    [Fact]
    public async Task Handle_OwnToken_RevokesItsFamily()
    {
        var token = StoredToken();
        _context.RefreshTokens.FindByHashAsync("hash-of-mine", Arg.Any<CancellationToken>()).Returns(token);

        var result = await Handler().Handle(new LogoutCommand("mine"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await _context.RefreshTokens.Received(1).RevokeFamilyAsync(token.FamilyId, Now, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_UnknownToken_SucceedsAndRevokesNothing()
    {
        var result = await Handler().Handle(new LogoutCommand("unknown"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await _context.RefreshTokens.DidNotReceiveWithAnyArgs().RevokeFamilyAsync(default, default, default);
    }

    [Fact]
    public async Task Handle_AnotherUsersToken_SucceedsAndRevokesNothing()
    {
        _context.RefreshTokens.FindByHashAsync("hash-of-theirs", Arg.Any<CancellationToken>())
            .Returns(StoredToken(userId: Guid.CreateVersion7()));

        var result = await Handler().Handle(new LogoutCommand("theirs"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await _context.RefreshTokens.DidNotReceiveWithAnyArgs().RevokeFamilyAsync(default, default, default);
    }

    [Fact]
    public async Task Handle_NoCurrentUser_ReturnsUnauthenticated()
    {
        _context.CurrentUser.UserId.Returns((Guid?)null);

        var result = await Handler().Handle(new LogoutCommand("mine"), CancellationToken.None);

        result.Error.Should().Be(AuthErrors.Unauthenticated);
    }

    [Fact]
    public async Task Handle_RevokedOwnToken_StillRevokesTheFamily()
    {
        var token = StoredToken();
        token.Rotate("hash-next", Now, TimeSpan.FromDays(30));
        _context.RefreshTokens.FindByHashAsync("hash-of-mine", Arg.Any<CancellationToken>()).Returns((RefreshToken?)token);

        var result = await Handler().Handle(new LogoutCommand("mine"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await _context.RefreshTokens.Received(1).RevokeFamilyAsync(token.FamilyId, Now, Arg.Any<CancellationToken>());
    }

    private LogoutCommandHandler Handler() => new(_context.RefreshTokens, _context.Tokens, _context.CurrentUser, _context.Clock);
}
