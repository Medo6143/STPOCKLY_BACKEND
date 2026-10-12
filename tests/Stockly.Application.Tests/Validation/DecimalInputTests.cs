using Stockly.Application.Validation;
using Stockly.Domain.Common;

namespace Stockly.Application.Tests.Validation;

/// <summary>The shared decimal-string parser (contract §1.2, brief decision 5).</summary>
public sealed class DecimalInputTests
{
    [Theory]
    [InlineData("0")]
    [InlineData("12.50")]
    [InlineData("-2.000")]
    [InlineData("12345678901234567")]
    [InlineData("1.0000000000000000000000000000000001")]
    public void IsWellFormed_ContractShape_IsTrue(string text)
    {
        DecimalInput.IsWellFormed(text).Should().BeTrue();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" 1")]
    [InlineData("1 ")]
    [InlineData("+1")]
    [InlineData("1.")]
    [InlineData(".5")]
    [InlineData("1,5")]
    [InlineData("1e3")]
    [InlineData("--1")]
    [InlineData("123456789012345678")]
    [InlineData("١٢")]
    [InlineData("abc")]
    public void IsWellFormed_OtherText_IsFalse(string? text)
    {
        DecimalInput.IsWellFormed(text).Should().BeFalse();
    }

    [Theory]
    [InlineData("12.50", 12.50)]
    [InlineData("12.5", 12.5)]
    [InlineData("0", 0)]
    [InlineData("-0.00", 0)]
    [InlineData("7.000000", 7)]
    public void Money_Valid_ParsesExactly(string text, decimal expected)
    {
        DecimalInput.Money(text, "costPrice").Value.Should().Be(expected);
    }

    [Fact]
    public void Money_LargestValue_ParsesWithoutRounding()
    {
        DecimalInput.Money("9999999999999999.99", "costPrice").Value.Should().Be(9_999_999_999_999_999.99m);
    }

    [Theory]
    [InlineData("-0.01", "Precision.Negative")]
    [InlineData("-1.999", "Precision.Negative")]
    [InlineData("1.001", "Precision.TooManyDecimals")]
    [InlineData("1.0000000000000000000000000000000001", "Precision.TooManyDecimals")]
    [InlineData("10000000000000000", "Precision.OutOfRange")]
    public void Money_Invalid_ReturnsPrecisionCode(string text, string code)
    {
        DecimalInput.Money(text, "costPrice").Error.Code.Should().Be(code);
    }

    [Fact]
    public void Money_TooManyDecimals_NamesTheFieldAndLimit()
    {
        DecimalInput.Money("1.001", "costPrice").Error
            .Should().Be(PrecisionErrors.TooManyDecimals("costPrice", Precision.MoneyDecimals));
    }

    [Theory]
    [InlineData("-2.000", -2)]
    [InlineData("1.5", 1.5)]
    [InlineData("99999999999.999", 99999999999.999)]
    public void Quantity_Valid_ParsesWithSign(string text, decimal expected)
    {
        DecimalInput.Quantity(text, "quantityChange").Value.Should().Be(expected);
    }

    [Theory]
    [InlineData("1.0001", "Precision.TooManyDecimals")]
    [InlineData("-1.0001", "Precision.TooManyDecimals")]
    [InlineData("100000000000", "Precision.OutOfRange")]
    [InlineData("-100000000000", "Precision.OutOfRange")]
    public void Quantity_Invalid_ReturnsPrecisionCode(string text, string code)
    {
        DecimalInput.Quantity(text, "quantityChange").Error.Code.Should().Be(code);
    }

    [Theory]
    [InlineData("-1", "Precision.Negative")]
    [InlineData("-0.0001", "Precision.Negative")]
    [InlineData("0.0001", "Precision.TooManyDecimals")]
    public void NonNegativeQuantity_Invalid_ReturnsPrecisionCode(string text, string code)
    {
        DecimalInput.NonNegativeQuantity(text, "countedQuantity").Error.Code.Should().Be(code);
    }

    [Theory]
    [InlineData("0.000", 0)]
    [InlineData("-0", 0)]
    [InlineData("26.000", 26)]
    public void NonNegativeQuantity_ZeroOrPositive_Parses(string text, decimal expected)
    {
        DecimalInput.NonNegativeQuantity(text, "countedQuantity").Value.Should().Be(expected);
    }

    [Theory]
    [InlineData("0", "Precision.NotPositive")]
    [InlineData("0.000000", "Precision.NotPositive")]
    [InlineData("-3", "Precision.NotPositive")]
    [InlineData("1.0000001", "Precision.TooManyDecimals")]
    [InlineData("100000000", "Precision.OutOfRange")]
    public void Factor_Invalid_ReturnsPrecisionCode(string text, string code)
    {
        DecimalInput.Factor(text, "factor").Error.Code.Should().Be(code);
    }

    [Fact]
    public void Factor_Valid_Parses()
    {
        DecimalInput.Factor("0.333333", "factor").Value.Should().Be(0.333333m);
    }

    [Fact]
    public void Parse_MalformedText_ReturnsValidationFailedForTheField()
    {
        var error = DecimalInput.Money("12,5", "costPrice").Error;

        error.Code.Should().Be(ValidationFailure.Code);
        error.FieldErrors!.Keys.Should().Equal("costPrice");
    }

    [Fact]
    public void FirstError_ReturnsTheFirstFailureInOrder()
    {
        var first = DecimalInput.Money("-1", "a");
        var second = DecimalInput.Money("1.001", "b");

        DecimalInput.FirstError(Result.Success(1m), first, second).Should().Be(first.Error);
        DecimalInput.FirstError(Result.Success(1m)).Should().BeNull();
    }
}
