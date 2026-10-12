using Stockly.Domain.Products;

namespace Stockly.Application.Features.Products;

/// <summary>Request limits of contract §5 (step 1.6).</summary>
public static class ProductLimits
{
    public const int SearchMaxLength = 100;
    public const int BarcodeMaxLength = Product.BarcodeMaxLength;
    public const int CategoryMaxLength = Product.CategoryMaxLength;
    public const int AdjustmentNoteMaxLength = 500;
}
