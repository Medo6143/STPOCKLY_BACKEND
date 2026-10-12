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
