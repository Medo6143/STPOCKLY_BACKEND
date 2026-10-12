using FluentValidation;
using Stockly.Application.Validation;
using Stockly.Domain.Common;

namespace Stockly.Application.Features.Products;

/// <summary>The P6/P7 unit body shared by <c>AddProductUnitCommand</c> and <c>UpdateProductUnitCommand</c>.</summary>
public interface IProductUnitInput
{
    string Name { get; }

    string Factor { get; }

    string SellPrice { get; }

    string? Barcode { get; }
}

/// <summary>Unit request shape. A blank name and precision rules return their own codes from the handler.</summary>
public abstract class ProductUnitInputValidator<TCommand> : AbstractValidator<TCommand>
    where TCommand : IProductUnitInput
{
    protected ProductUnitInputValidator()
    {
        RuleFor(c => c.Name).NotNull();
        RuleFor(c => c.Factor).NotNull().DecimalString();
        RuleFor(c => c.SellPrice).NotNull().DecimalString();
    }
}

public static class ProductUnitInputExtensions
{
    /// <summary>The factor (<c>Precision.NotPositive</c> when &lt;= 0) and the sell price (money), parsed for the Domain.</summary>
    public static Result<(decimal Factor, decimal SellPrice)> ParseValues(this IProductUnitInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var factor = DecimalInput.Factor(input.Factor, FieldPath.ToCamelCase(nameof(input.Factor)));
        var sellPrice = DecimalInput.Money(input.SellPrice, FieldPath.ToCamelCase(nameof(input.SellPrice)));
        return DecimalInput.FirstError(factor, sellPrice) is { } error
            ? error
            : (factor.Value, sellPrice.Value);
    }
}
