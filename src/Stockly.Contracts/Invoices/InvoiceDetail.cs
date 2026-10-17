namespace Stockly.Contracts.Invoices;

/// <summary>
/// One invoice with its lines (contract §6, I3 and the I4 response). Money strings have 2 decimals; dates are
/// <c>yyyy-MM-dd</c>; <see cref="Items"/> are ordered by line number and empty while processing.
/// </summary>
public sealed record InvoiceDetail(
    Guid Id,
    Guid ClientRequestId,
    InvoiceStatus Status,
    Guid? SupplierId,
    string? SupplierName,
    string? InvoiceNumber,
    DateOnly? InvoiceDate,
    string? TotalAmount,
    string? ComputedTotal,
    IReadOnlyList<InvoiceWarning> Warnings,
    string? FailureReason,
    IReadOnlyList<InvoiceItemDto> Items,
    DateTime CreatedAt,
    DateTime? ProcessedAt,
    DateTime? ConfirmedAt);

/// <summary>
/// One invoice line (contract §6). <see cref="ProductId"/> is the existing product, or after confirm the product created
/// from <see cref="NewProduct"/>; <see cref="Quantity"/> is in the line's unit (3 decimals); <see cref="MatchConfidence"/>
/// has 2 decimals.
/// </summary>
public sealed record InvoiceItemDto(
    Guid Id,
    int LineNo,
    string RawName,
    Guid? ProductId,
    string? ProductName,
    Guid? ProductUnitId,
    string? UnitName,
    string Quantity,
    string UnitPrice,
    string LineTotal,
    DateOnly? ExpiryDate,
    string? BatchNo,
    string? MatchConfidence,
    InvoiceNewProductDto? NewProduct);

/// <summary>A product to create on confirm (contract I4 <c>newProduct</c>): name, base unit name, base unit sell price (money string).</summary>
public sealed record InvoiceNewProductDto(string Name, string BaseUnitName, string SellPrice);
