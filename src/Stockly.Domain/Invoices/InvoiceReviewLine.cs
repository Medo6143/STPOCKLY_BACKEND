namespace Stockly.Domain.Invoices;

/// <summary>
/// One line of the review edit (contract I4): its number, the name as written, quantity in the line's unit and price of
/// one unit, and either an existing product with one of its units (<see cref="Product"/>) or a product to create on
/// confirm (<see cref="NewProduct"/>), never both and never neither.
/// </summary>
public sealed record InvoiceReviewLine(
    int LineNo,
    string RawName,
    decimal Quantity,
    decimal UnitPrice,
    DateOnly? ExpiryDate,
    string? BatchNo,
    InvoiceLineProduct? Product,
    InvoiceNewProduct? NewProduct);

/// <summary>An existing product of the shop and the unit of the line's quantity.</summary>
public sealed record InvoiceLineProduct(Guid ProductId, Guid ProductUnitId);

/// <summary>
/// A product to create on confirm (docs/design/backend.md §10.5): its name, base unit name and base unit sell price. The
/// line's quantity is in this base unit and its unit price becomes the cost price.
/// </summary>
public sealed record InvoiceNewProduct(string Name, string BaseUnitName, decimal SellPrice);
