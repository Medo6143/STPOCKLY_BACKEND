using System.Globalization;
using Stockly.Application.Features.Auth.Login;
using Stockly.Application.Features.Auth.Logout;
using Stockly.Application.Features.Auth.Refresh;
using Stockly.Application.Features.Auth.Register;
using Stockly.Application.Validation;
using Stockly.Contracts.Auth;

namespace Stockly.Application.Tests.Features.Auth;

public sealed class AuthValidatorTests
{
    [Fact]
    public void Register_ValidCommand_Passes()
    {
        new RegisterCommandValidator().Validate(Register()).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Register_ContractLimits_AreEnforcedPerField()
    {
        var command = Register() with
        {
            FullName = new string('a', 201),
            Email = new string('a', 250) + "@example.com",
            Phone = new string('1', 31),
            DeviceId = new string('d', 101),
            PreferredLanguage = "fr",
            Shop = new RegisterShopRequest(new string('s', 201), "", null, null, null),
        };

        var failures = new RegisterCommandValidator().Validate(command).Errors.Select(error => FieldPath.ToCamelCase(error.PropertyName));

        failures.Distinct().Should().BeEquivalentTo(
            "fullName", "email", "phone", "deviceId", "preferredLanguage", "shop.name", "shop.shopType");
    }

    [Theory]
    [InlineData("abcdefgh")]
    [InlineData("12345678")]
    [InlineData("a1")]
    [InlineData("كلمةسر١٢٣٤")]
    public void Register_WeakPassword_FailsOnPassword(string password)
    {
        var result = new RegisterCommandValidator().Validate(Register() with { Password = password });

        result.Errors.Should().OnlyContain(error => error.PropertyName == "Password").And.NotBeEmpty();
    }

    [Fact]
    public void Register_PasswordOver128_Fails()
    {
        new RegisterCommandValidator().Validate(Register() with { Password = "a1" + new string('x', 127) }).IsValid.Should().BeFalse();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("ar")]
    [InlineData("en")]
    public void Register_SupportedLanguages_Pass(string? language)
    {
        new RegisterCommandValidator().Validate(Register() with { PreferredLanguage = language }).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Register_MissingShop_FailsOnShop()
    {
        var result = new RegisterCommandValidator().Validate(Register() with { Shop = null! });

        result.Errors.Select(error => error.PropertyName).Should().Equal("Shop");
    }

    [Fact]
    public void Register_BlankShopName_IsLeftToTheDomain()
    {
        new RegisterCommandValidator().Validate(Register() with { Shop = new RegisterShopRequest("  ", "Pharmacy", null, null, null) })
            .IsValid.Should().BeTrue("a blank name is Shop.NameRequired, not Validation.Failed");
    }

    [Fact]
    public void Register_CustomMessages_FollowTheUiCulture()
    {
        var previous = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = new CultureInfo("en");
            var english = new RegisterCommandValidator().Validate(Register() with { Password = "abcdefgh" }).Errors.Single().ErrorMessage;
            CultureInfo.CurrentUICulture = new CultureInfo("ar");
            var arabic = new RegisterCommandValidator().Validate(Register() with { Password = "abcdefgh" }).Errors.Single().ErrorMessage;

            english.Should().Be("Password must contain at least one digit (0-9).");
            arabic.Should().Be("يجب أن تحتوي كلمة المرور على رقم واحد على الأقل (0-9).");
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
        }
    }

    [Fact]
    public void Login_RequiresEmailAndPassword()
    {
        var result = new LoginCommandValidator().Validate(new LoginCommand("", "", new string('d', 101)));

        result.Errors.Select(error => error.PropertyName).Distinct().Should().BeEquivalentTo("Email", "Password", "DeviceId");
        new LoginCommandValidator().Validate(new LoginCommand("a@b.c", "x", null)).IsValid.Should().BeTrue();
    }

    [Fact]
    public void RefreshAndLogout_RequireABoundedToken()
    {
        new RefreshCommandValidator().Validate(new RefreshCommand("")).IsValid.Should().BeFalse();
        new RefreshCommandValidator().Validate(new RefreshCommand(new string('t', 201))).IsValid.Should().BeFalse();
        new RefreshCommandValidator().Validate(new RefreshCommand("token")).IsValid.Should().BeTrue();
        new LogoutCommandValidator().Validate(new LogoutCommand("")).IsValid.Should().BeFalse();
        new LogoutCommandValidator().Validate(new LogoutCommand("token")).IsValid.Should().BeTrue();
    }

    private static RegisterCommand Register() =>
        new("Mona Adel", "mona@example.com", "Str0ngPass", "01001234567", null, "pixel-7", new RegisterShopRequest("صيدلية الشفاء", "Pharmacy", null, null, null));
}
