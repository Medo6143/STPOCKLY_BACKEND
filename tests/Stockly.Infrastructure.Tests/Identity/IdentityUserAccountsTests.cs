using Microsoft.Extensions.DependencyInjection;
using Stockly.Application.Abstractions.Identity;
using Stockly.Application.Abstractions.Persistence;
using Stockly.Application.Features.Auth;
using Stockly.Domain.Common;
using Stockly.Domain.Users;
using Stockly.Infrastructure.Tests.Support;

namespace Stockly.Infrastructure.Tests.Identity;

/// <summary><c>IUserAccounts</c> on ASP.NET Core Identity and the real <c>users</c> table.</summary>
[Collection(PostgresCollectionDefinition.Name)]
public sealed class IdentityUserAccountsTests(PostgresFixture fixture)
{
    private const string Password = "Str0ngPass";

    [Fact]
    public async Task Create_ThenCheckCredentials_ReturnsTheAccountWithHashedPassword()
    {
        var email = NewEmail();

        var created = await CreateAsync(email, PreferredLanguage.En);

        created.IsSuccess.Should().BeTrue();
        var check = await UseAccountsAsync(accounts => accounts.CheckCredentialsAsync(email.ToUpperInvariant(), Password, CancellationToken.None));
        check.Status.Should().Be(CredentialStatus.Valid);
        check.User.Should().BeEquivalentTo(new UserAccount(created.Value, email, "Mona Adel", "0100", PreferredLanguage.En));
        (await fixture.ScalarAsync<string>("SELECT password_hash FROM users WHERE id = @p0", created.Value))
            .Should().NotBeNullOrEmpty().And.NotContain(Password);
        (await fixture.ScalarAsync<string>("SELECT phone FROM users WHERE id = @p0", created.Value)).Should().Be("0100");
    }

    [Fact]
    public async Task Create_SameEmailOtherCase_ReturnsEmailTaken()
    {
        var email = NewEmail();
        await CreateAsync(email, PreferredLanguage.Ar);

        var second = await CreateAsync(email.ToUpperInvariant(), PreferredLanguage.Ar);

        second.Error.Should().Be(AuthErrors.EmailTaken);
    }

    [Fact]
    public async Task Create_SameEmailConcurrently_OneSucceedsOneIsEmailTaken()
    {
        var email = NewEmail();

        var results = await Task.WhenAll(CreateInTransactionAsync(email), CreateInTransactionAsync(email));

        results.Count(result => result.IsSuccess).Should().Be(1);
        results.Single(result => result.IsFailure).Error.Should().Be(AuthErrors.EmailTaken);
    }

    [Fact]
    public async Task Create_PasswordIdentityRejects_ReturnsValidationFailedOnPassword()
    {
        var result = await UseAccountsAsync(accounts => accounts.CreateAsync(
            new NewUserAccount(NewEmail(), "Mona", null, PreferredLanguage.Ar, TestTime.T0), "noDigitsHere", CancellationToken.None));

        result.Error.Code.Should().Be(ValidationFailure.Code);
        result.Error.FieldErrors.Should().ContainKey("password");
    }

    [Theory]
    [InlineData("Wr0ngPass", CredentialStatus.Invalid)]
    [InlineData(Password, CredentialStatus.Valid)]
    public async Task CheckCredentials_ChecksThePassword(string password, CredentialStatus expected)
    {
        var email = NewEmail();
        await CreateAsync(email, PreferredLanguage.Ar);

        var check = await UseAccountsAsync(accounts => accounts.CheckCredentialsAsync(email, password, CancellationToken.None));

        check.Status.Should().Be(expected);
        (check.User is not null).Should().Be(expected == CredentialStatus.Valid);
    }

    [Fact]
    public async Task CheckCredentials_UnknownEmail_ReturnsInvalid()
    {
        (await UseAccountsAsync(accounts => accounts.CheckCredentialsAsync(NewEmail(), Password, CancellationToken.None)))
            .Should().Be(CredentialCheck.Invalid);
    }

    [Fact]
    public async Task CheckCredentials_FiveWrongPasswords_LocksForFifteenMinutesOfTheClockEvenForTheRightPassword()
    {
        var clock = new FakeClock(TestTime.T0);
        var email = NewEmail();
        var created = await CreateAsync(email, PreferredLanguage.Ar);

        for (var attempt = 1; attempt <= AuthProtection.MaxFailedLoginAttempts; attempt++)
        {
            (await CheckAsync(clock, email, "Wr0ngPass")).Status.Should().Be(CredentialStatus.Invalid);
        }

        var locked = await CheckAsync(clock, email, Password);

        locked.Should().Be(CredentialCheck.LockedOut(TestTime.T0 + AuthProtection.LockoutDuration));
        (await fixture.ScalarAsync<DateTime>("SELECT lockout_end FROM users WHERE id = @p0", created.Value))
            .Should().Be((TestTime.T0 + AuthProtection.LockoutDuration).UtcDateTime);

        clock.UtcNow = TestTime.T0 + AuthProtection.LockoutDuration - TimeSpan.FromSeconds(1);
        (await CheckAsync(clock, email, Password)).Status.Should().Be(CredentialStatus.LockedOut);

        clock.UtcNow = TestTime.T0 + AuthProtection.LockoutDuration + TimeSpan.FromSeconds(1);
        (await CheckAsync(clock, email, Password)).Status.Should().Be(CredentialStatus.Valid);
    }

    [Fact]
    public async Task CheckCredentials_RightPassword_ResetsTheFailedCount()
    {
        var clock = new FakeClock(TestTime.T0);
        var email = NewEmail();
        var created = await CreateAsync(email, PreferredLanguage.Ar);

        for (var attempt = 1; attempt < AuthProtection.MaxFailedLoginAttempts; attempt++)
        {
            await CheckAsync(clock, email, "Wr0ngPass");
        }

        (await CheckAsync(clock, email, Password)).Status.Should().Be(CredentialStatus.Valid);
        (await fixture.ScalarAsync<int>("SELECT access_failed_count FROM users WHERE id = @p0", created.Value)).Should().Be(0);

        for (var attempt = 1; attempt < AuthProtection.MaxFailedLoginAttempts; attempt++)
        {
            await CheckAsync(clock, email, "Wr0ngPass");
        }

        (await CheckAsync(clock, email, Password)).Status.Should().Be(CredentialStatus.Valid);
    }

    [Fact]
    public async Task CheckCredentials_UnknownEmailManyTimes_NeverLocked()
    {
        var email = NewEmail();

        for (var attempt = 0; attempt <= AuthProtection.MaxFailedLoginAttempts; attempt++)
        {
            (await UseAccountsAsync(accounts => accounts.CheckCredentialsAsync(email, Password, CancellationToken.None)))
                .Should().Be(CredentialCheck.Invalid);
        }
    }

    [Fact]
    public async Task FindById_AndRecordSignIn_ReadAndWriteTheUser()
    {
        var created = await CreateAsync(NewEmail(), PreferredLanguage.Ar);

        await UseAccountsAsync(async accounts =>
        {
            await accounts.RecordSignInAsync(created.Value, TestTime.T2, CancellationToken.None);
            return true;
        });

        (await UseAccountsAsync(accounts => accounts.FindByIdAsync(created.Value, CancellationToken.None)))!.Id.Should().Be(created.Value);
        (await UseAccountsAsync(accounts => accounts.FindByIdAsync(Guid.CreateVersion7(), CancellationToken.None))).Should().BeNull();
        (await fixture.ScalarAsync<DateTime>("SELECT last_login_at FROM users WHERE id = @p0", created.Value))
            .Should().Be(TestTime.T2.UtcDateTime);
    }

    private static string NewEmail() => $"user-{Guid.NewGuid():N}@example.com";

    private Task<Result<Guid>> CreateAsync(string email, PreferredLanguage language) =>
        UseAccountsAsync(accounts => accounts.CreateAsync(
            new NewUserAccount(email, "Mona Adel", "0100", language, TestTime.T0), Password, CancellationToken.None));

    private async Task<Result<Guid>> CreateInTransactionAsync(string email)
    {
        await using var scope = fixture.CreateScope(shopId: null, withIdentity: true);
        var accounts = scope.ServiceProvider.GetRequiredService<IUserAccounts>();
        return await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().ExecuteInTransactionAsync(
            token => accounts.CreateAsync(new NewUserAccount(email, "Mona", null, PreferredLanguage.Ar, TestTime.T0), Password, token),
            result => result.IsSuccess,
            CancellationToken.None);
    }

    private Task<CredentialCheck> CheckAsync(FakeClock clock, string email, string password) =>
        UseAccountsAsync(accounts => accounts.CheckCredentialsAsync(email, password, CancellationToken.None), clock);

    private async Task<T> UseAccountsAsync<T>(Func<IUserAccounts, Task<T>> use, FakeClock? clock = null)
    {
        await using var scope = fixture.CreateScope(shopId: null, withIdentity: true, clock: clock);
        return await use(scope.ServiceProvider.GetRequiredService<IUserAccounts>());
    }
}
