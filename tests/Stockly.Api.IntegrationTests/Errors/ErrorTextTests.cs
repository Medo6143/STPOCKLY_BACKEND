using System.Globalization;
using System.Reflection;
using Stockly.Api.Errors;
using Stockly.Application.Features.Auth;
using Stockly.Domain.Common;
using Stockly.Domain.Shops;

namespace Stockly.Api.IntegrationTests.Errors;

/// <summary>Every code the API can return in step 1.5 has a title and detail in Arabic and English (contract §1.5, §9).</summary>
public sealed class ErrorTextTests
{
    public static TheoryData<string> Codes()
    {
        var authCodes = typeof(AuthErrors).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Select(field => ((Error)field.GetValue(null)!).Code);
        string[] others =
        [
            ValidationFailure.Code,
            StocklyProblems.ServerUnexpected,
            "Http.NotFound",
            "Http.MethodNotAllowed",
            "Http.UnsupportedMediaType",
            ShopErrors.NameRequired.Code,
            ShopErrors.UnknownType.Code,
            ShopErrors.Suspended.Code,
        ];
        return new TheoryData<string>(authCodes.Concat(others));
    }

    [Theory]
    [MemberData(nameof(Codes))]
    public void TryGet_EveryCode_HasArabicAndEnglishText(string code)
    {
        var english = InCulture("en", () => (ErrorText.TryGet(code, out var title, out var detail), title, detail));
        var arabic = InCulture("ar", () => (ErrorText.TryGet(code, out var title, out var detail), title, detail));

        english.Item1.Should().BeTrue("{0} needs English text", code);
        arabic.Item1.Should().BeTrue("{0} needs Arabic text", code);
        arabic.detail.Should().NotBe(english.detail, "{0} must be translated, not copied", code);
    }

    [Fact]
    public void InvalidValue_IsLocalized()
    {
        InCulture("ar", () => ErrorText.InvalidValue).Should().NotBe(InCulture("en", () => ErrorText.InvalidValue));
    }

    [Theory]
    [InlineData(404, "Http.NotFound")]
    [InlineData(405, "Http.MethodNotAllowed")]
    [InlineData(415, "Http.UnsupportedMediaType")]
    [InlineData(400, "Validation.Failed")]
    [InlineData(401, "Auth.Unauthenticated")]
    [InlineData(403, "Auth.Forbidden")]
    [InlineData(500, "Server.Unexpected")]
    [InlineData(503, "Server.Unexpected")]
    public void DefaultCodeFor_FrameworkStatus_IsTheContractCode(int status, string code)
    {
        StocklyProblems.DefaultCodeFor(status).Should().Be(code);
    }

    [Theory]
    [InlineData(ErrorType.Validation, 400)]
    [InlineData(ErrorType.Unauthorized, 401)]
    [InlineData(ErrorType.Forbidden, 403)]
    [InlineData(ErrorType.NotFound, 404)]
    [InlineData(ErrorType.Conflict, 409)]
    [InlineData(ErrorType.Failure, 500)]
    public void StatusFor_ErrorType_FollowsContract(ErrorType type, int status)
    {
        StocklyProblems.StatusFor(type).Should().Be(status);
    }

    private static T InCulture<T>(string name, Func<T> read)
    {
        var previous = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = new CultureInfo(name);
        try
        {
            return read();
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
        }
    }
}
