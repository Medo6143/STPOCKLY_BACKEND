using Stockly.Domain.Common;
using Stockly.Domain.Shops;

namespace Stockly.Domain.Tests.Common;

public sealed class ErrorTests
{
    [Fact]
    public void Factories_SetTheMatchingType()
    {
        Error.Forbidden("A.B", "m").Type.Should().Be(ErrorType.Forbidden);
        Error.Unauthorized("A.B", "m").Type.Should().Be(ErrorType.Unauthorized);
        Error.Validation("A.B", "m").FieldErrors.Should().BeNull();
        Error.Locked("A.B", "m").Type.Should().Be(ErrorType.Locked);
        Error.TooManyRequests("A.B", "m").Type.Should().Be(ErrorType.TooManyRequests);
        Error.Validation("A.B", "m").RetryAfter.Should().BeNull();
    }

    [Fact]
    public void RetryAfter_WithExpression_KeepsCodeAndType()
    {
        var error = Error.Locked("A.B", "m") with { RetryAfter = TimeSpan.FromSeconds(90) };

        error.Code.Should().Be("A.B");
        error.Type.Should().Be(ErrorType.Locked);
        error.RetryAfter.Should().Be(TimeSpan.FromSeconds(90));
    }

    [Fact]
    public void ValidationFailure_WithFieldErrors_IsValidationFailedWithFields()
    {
        var fields = new Dictionary<string, string[]> { ["shop.name"] = ["required"] };

        var error = ValidationFailure.WithFieldErrors(fields);

        error.Code.Should().Be("Validation.Failed");
        error.Type.Should().Be(ErrorType.Validation);
        error.FieldErrors.Should().BeSameAs(fields);
    }

    [Fact]
    public void ShopSuspended_IsForbidden()
    {
        ShopErrors.Suspended.Code.Should().Be("Shop.Suspended");
        ShopErrors.Suspended.Type.Should().Be(ErrorType.Forbidden);
    }
}
