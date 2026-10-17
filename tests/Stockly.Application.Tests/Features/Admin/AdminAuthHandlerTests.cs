using NSubstitute;
using Stockly.Application.Abstractions.Identity;
using Stockly.Application.Features.Admin.AdminLogin;
using Stockly.Application.Features.Admin.AdminRefresh;
using Stockly.Application.Features.Auth;
using Stockly.Application.Tests.Support;
using Stockly.Domain.Users;
using static Stockly.Application.Tests.Support.AdminTestContext;

namespace Stockly.Application.Tests.Features.Admin;

/// <summary>AD1 and AD2: admin sessions on the shop session rules (lockout, rotation, families), only for platform admins.</summary>
public sealed class AdminAuthHandlerTests
{
    private readonly AdminTestContext _context = new();

    [Fact]
    public async Task Login_PlatformAdmin_StartsAFamilyWithAnAdminToken()
    {
        _context.Users.CheckCredentialsAsync("admin@example.com", "Str0ngPass", Arg.Any<CancellationToken>())
            .Returns(CredentialCheck.Valid(AdminAccount));
        _context.Admins.IsPlatformAdminAsync(AdminId, Arg.Any<CancellationToken>()).Returns(true);
        RefreshToken? stored = null;
        _context.RefreshTokens.Add(Arg.Do<RefreshToken>(token => stored = token));

        var result = await Login(" admin@example.com ");

        result.IsSuccess.Should().BeTrue();
        result.Value.AccessToken.Should().Be("admin-access");
        result.Value.RefreshToken.Should().Be("refresh-1");
        result.Value.User.Should().BeEquivalentTo(new { Id = AdminId, Email = "admin@example.com", FullName = "Platform Admin" });
        stored!.FamilyId.Should().Be(stored.Id);
        stored.UserId.Should().Be(AdminId);
        _context.Tokens.Received(1).CreateAdminAccessToken(AdminId);
        _context.Tokens.DidNotReceiveWithAnyArgs().CreateAccessToken(default, default, default);
        await _context.Users.Received(1).RecordSignInAsync(AdminId, Now, Arg.Any<CancellationToken>());
        _context.UnitOfWork.SaveCount.Should().Be(1);
    }

    [Fact]
    public async Task Login_ValidShopUser_IsInvalidCredentialsSoAdminsAreNotRevealed()
    {
        _context.Users.CheckCredentialsAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(CredentialCheck.Valid(AdminAccount));
        _context.Admins.IsPlatformAdminAsync(AdminId, Arg.Any<CancellationToken>()).Returns(false);

        var result = await Login("admin@example.com");

        result.Error.Should().Be(AuthErrors.InvalidCredentials);
        _context.RefreshTokens.DidNotReceiveWithAnyArgs().Add(default!);
    }

    [Fact]
    public async Task Login_WrongPassword_IsInvalidCredentials()
    {
        _context.Users.CheckCredentialsAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(CredentialCheck.Invalid);

        (await Login("admin@example.com")).Error.Should().Be(AuthErrors.InvalidCredentials);
        await _context.Admins.DidNotReceiveWithAnyArgs().IsPlatformAdminAsync(default, default);
    }

    [Fact]
    public async Task Login_LockedOut_IsLockedOutWithRetryAfter()
    {
        _context.Users.CheckCredentialsAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(CredentialCheck.LockedOut(Now.AddMinutes(10)));

        var result = await Login("admin@example.com");

        result.Error.Code.Should().Be(AuthErrors.LockedOut.Code);
        result.Error.RetryAfter.Should().Be(TimeSpan.FromMinutes(10));
    }

    [Fact]
    public void LoginCommand_CommitsOnFailure_SoFailedAttemptsCount()
    {
        new AdminLoginCommand("a", "b").Should().BeAssignableTo<Abstractions.Messaging.ICommitOnFailure>();
    }

    [Fact]
    public async Task Refresh_AdminToken_RotatesWithinTheFamily()
    {
        var current = StoredToken();
        ArrangeToken(current, isAdmin: true);
        RefreshToken? successor = null;
        _context.RefreshTokens.Add(Arg.Do<RefreshToken>(token => successor = token));

        var result = await Refresh();

        result.Value.RefreshToken.Should().Be("refresh-1");
        current.RevokedAt.Should().Be(Now);
        successor!.FamilyId.Should().Be(current.FamilyId);
        _context.UnitOfWork.SaveCount.Should().Be(1);
    }

    [Fact]
    public async Task Refresh_ShopUsersToken_IsInvalidRefreshToken()
    {
        ArrangeToken(StoredToken(), isAdmin: false);

        (await Refresh()).Error.Should().Be(AuthErrors.InvalidRefreshToken);
        _context.RefreshTokens.DidNotReceiveWithAnyArgs().Add(default!);
    }

    [Fact]
    public async Task Refresh_UnknownToken_IsInvalidRefreshToken()
    {
        (await Refresh()).Error.Should().Be(AuthErrors.InvalidRefreshToken);
    }

    [Fact]
    public async Task Refresh_RevokedAdminToken_RevokesTheFamilyAndIsReused()
    {
        var current = StoredToken();
        current.Rotate("hash-next", Now.AddHours(-1), TokenLifetimes.RefreshToken);
        ArrangeToken(current, isAdmin: true);

        var result = await Refresh();

        result.Error.Should().Be(AuthErrors.RefreshTokenReused);
        await _context.RefreshTokens.Received(1).RevokeFamilyAsync(current.FamilyId, Now, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Refresh_ExpiredAdminToken_IsInvalidRefreshToken()
    {
        ArrangeToken(StoredToken(createdAt: Now.AddDays(-31)), isAdmin: true);

        (await Refresh()).Error.Should().Be(AuthErrors.InvalidRefreshToken);
    }

    [Fact]
    public void Validators_RejectBlankFields()
    {
        new AdminLoginCommandValidator().Validate(new AdminLoginCommand("", "")).Errors.Should().HaveCount(2);
        new AdminRefreshCommandValidator().Validate(new AdminRefreshCommand("")).IsValid.Should().BeFalse();
    }

    private static RefreshToken StoredToken(DateTimeOffset? createdAt = null) =>
        RefreshToken.StartFamily(AdminId, "hash-of-presented", null, createdAt ?? Now.AddDays(-1), TokenLifetimes.RefreshToken);

    private void ArrangeToken(RefreshToken token, bool isAdmin)
    {
        _context.RefreshTokens.FindByHashForUpdateAsync("hash-of-presented", Arg.Any<CancellationToken>()).Returns(token);
        _context.Admins.IsPlatformAdminAsync(AdminId, Arg.Any<CancellationToken>()).Returns(isAdmin);
        _context.Users.FindByIdAsync(AdminId, Arg.Any<CancellationToken>()).Returns(AdminAccount);
    }

    private Task<Domain.Common.Result<Contracts.Admin.AdminAuthResponse>> Login(string email) =>
        new AdminLoginCommandHandler(_context.Users, _context.Admins, _context.Sessions, _context.UnitOfWork, _context.Clock)
            .Handle(new AdminLoginCommand(email, "Str0ngPass"), CancellationToken.None);

    private Task<Domain.Common.Result<Contracts.Admin.AdminAuthResponse>> Refresh() =>
        new AdminRefreshCommandHandler(
                _context.RefreshTokens, _context.Users, _context.Admins, _context.Tokens, _context.Sessions, _context.UnitOfWork, _context.Clock)
            .Handle(new AdminRefreshCommand("presented"), CancellationToken.None);
}
