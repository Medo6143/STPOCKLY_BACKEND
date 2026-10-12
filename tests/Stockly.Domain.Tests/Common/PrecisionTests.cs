using Stockly.Domain.Common;

namespace Stockly.Domain.Tests.Common;

public sealed class PrecisionTests
{
    private const string Field = "value";

    public static TheoryData<decimal> ValidMoney => new() { 0m, 0.01m, 1.5m, 1.50m, 1.500m, 9_999_999_999_999_999.99m };

    public static TheoryData<decimal> ValidQuantity => new() { 0m, 0.001m, -0.001m, 1.5m, 1.250m, 1.2500m, -12.75m, 99_999_999_999.999m, -99_999_999_999.999m };

    public static TheoryData<decimal> ValidFactor => new() { 0.000001m, 0.5m, 1m, 3m, 1.123456m, 99_999_999.999999m };

    [Theory]
    [MemberData(nameof(ValidMoney))]
    public void ValidateMoney_WithinRules_ReturnsNull(decimal value)
    {
        Precision.ValidateMoney(value, Field).Should().BeNull();
    }

    [Fact]
    public void ValidateMoney_ThreeDecimals_RejectsWithoutRounding()
    {
        Precision.ValidateMoney(1.505m, Field).Should().Be(PrecisionErrors.TooManyDecimals(Field, Precision.MoneyDecimals));
    }

    [Fact]
    public void ValidateMoney_Negative_Rejects()
    {
        Precision.ValidateMoney(-0.01m, Field).Should().Be(PrecisionErrors.Negative(Field));
    }

    [Fact]
    public void ValidateMoney_DoesNotFitNumeric18Comma2_Rejects()
    {
        Precision.ValidateMoney(10_000_000_000_000_000m, Field).Should().Be(PrecisionErrors.OutOfRange(Field));
    }

    [Theory]
    [MemberData(nameof(ValidQuantity))]
    public void ValidateQuantity_WithinRules_ReturnsNull(decimal value)
    {
        Precision.ValidateQuantity(value, Field).Should().BeNull();
    }

    [Theory]
    [InlineData("1.0001")]
    [InlineData("-0.0005")]
    public void ValidateQuantity_FourDecimals_RejectsWithoutRounding(string value)
    {
        Precision.ValidateQuantity(decimal.Parse(value, System.Globalization.CultureInfo.InvariantCulture), Field)
            .Should().Be(PrecisionErrors.TooManyDecimals(Field, Precision.QuantityDecimals));
    }

    [Theory]
    [InlineData("100000000000")]
    [InlineData("-100000000000")]
    public void ValidateQuantity_DoesNotFitNumeric14Comma3_Rejects(string value)
    {
        Precision.ValidateQuantity(decimal.Parse(value, System.Globalization.CultureInfo.InvariantCulture), Field)
            .Should().Be(PrecisionErrors.OutOfRange(Field));
    }

    [Fact]
    public void ValidateNonNegativeQuantity_Negative_Rejects()
    {
        Precision.ValidateNonNegativeQuantity(-1m, Field).Should().Be(PrecisionErrors.Negative(Field));
    }

    [Fact]
    public void ValidateNonNegativeQuantity_ZeroAndPositive_ReturnsNull()
    {
        Precision.ValidateNonNegativeQuantity(0m, Field).Should().BeNull();
        Precision.ValidateNonNegativeQuantity(2.5m, Field).Should().BeNull();
    }

    [Fact]
    public void ValidateNonNegativeQuantity_TooManyDecimals_Rejects()
    {
        Precision.ValidateNonNegativeQuantity(1.0001m, Field)
            .Should().Be(PrecisionErrors.TooManyDecimals(Field, Precision.QuantityDecimals));
    }

    [Theory]
    [MemberData(nameof(ValidFactor))]
    public void ValidateFactor_WithinRules_ReturnsNull(decimal value)
    {
        Precision.ValidateFactor(value, Field).Should().BeNull();
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    public void ValidateFactor_NotPositive_Rejects(string value)
    {
        Precision.ValidateFactor(decimal.Parse(value, System.Globalization.CultureInfo.InvariantCulture), Field)
            .Should().Be(PrecisionErrors.NotPositive(Field));
    }

    [Fact]
    public void ValidateFactor_SevenDecimals_RejectsWithoutRounding()
    {
        Precision.ValidateFactor(0.0000001m, Field).Should().Be(PrecisionErrors.TooManyDecimals(Field, Precision.FactorDecimals));
    }

    [Fact]
    public void ValidateFactor_DoesNotFitNumeric14Comma6_Rejects()
    {
        Precision.ValidateFactor(100_000_000m, Field).Should().Be(PrecisionErrors.OutOfRange(Field));
    }

    [Fact]
    public void PrecisionErrors_AreValidationErrorsNamingTheField()
    {
        var error = PrecisionErrors.TooManyDecimals("costPrice", 2);

        error.Type.Should().Be(ErrorType.Validation);
        error.Message.Should().Contain("costPrice").And.Contain("2");
    }
}
