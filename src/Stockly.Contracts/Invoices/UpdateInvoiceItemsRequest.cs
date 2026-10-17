namespace Stockly.Contracts.Invoices;

/// <summary>
/// I4 <c>PUT /api/invoices/{id}/items</c>: replaces the header fields and all lines while the invoice is
/// <c>NeedsReview</c>. Money and quantities are decimal strings (contract §1.2). Optional fields have defaults so the spec
/// does not list them as required.
/// </summary>
public sealed record UpdateInvoiceItemsRequest(
    IReadOnlyList<InvoiceLineRequest> Items,
    string? SupplierName = null,
    string? InvoiceNumber = null,
    DateOnly? InvoiceDate = null,
    string? TotalAmount = null);

/// <summary>
/// One reviewed line: <see cref="ProductId"/> + <see cref="ProductUnitId"/> (an existing product and one of its units) or
/// <see cref="NewProduct"/> (created on confirm), never both. <see cref="Quantity"/> &gt; 0 in the line's unit,
/// <see cref="UnitPrice"/> &gt;= 0.
/// </summary>
public sealed record InvoiceLineRequest(
    int LineNo,
    string RawName,
    string Quantity,
    string UnitPrice,
    Guid? ProductId = null,
    Guid? ProductUnitId = null,
    InvoiceNewProductDto? NewProduct = null,
    DateOnly? ExpiryDate = null,
    string? BatchNo = null);
