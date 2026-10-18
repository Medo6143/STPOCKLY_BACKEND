using Stockly.Domain.Common;

namespace Stockly.Domain.Invoices;

/// <summary>
/// A photographed supplier invoice (docs/design/backend.md §1, §12.1). The scan creates it in
/// <see cref="InvoiceStatus.Processing"/>; the background job either completes the extraction
/// (<see cref="InvoiceStatus.NeedsReview"/>, with lines and warnings) or marks it <see cref="InvoiceStatus.Failed"/>.
/// The AI never changes stock (AGENTS.md rule 19): nothing here touches products.
/// </summary>
public sealed class Invoice : Entity, ITenantEntity
{
    private readonly List<InvoiceItem> _items = [];

    private Invoice(
        Guid shopId, Guid clientRequestId, Guid createdByUserId, string imageObjectKey, string imageSha256, DateTimeOffset createdAt)
    {
        ShopId = shopId;
        ClientRequestId = clientRequestId;
        CreatedByUserId = createdByUserId;
        ImageObjectKey = imageObjectKey;
        ImageSha256 = imageSha256;
        CreatedAt = createdAt;
        Status = InvoiceStatus.Processing;
    }

    public Guid ShopId { get; }

    /// <summary>Client-generated id of the scan; unique per shop (ADR-0006).</summary>
    public Guid ClientRequestId { get; }

    public Guid CreatedByUserId { get; }

    public InvoiceStatus Status { get; private set; }

    /// <summary>Key of the image in object storage (ADR-0008).</summary>
    public string ImageObjectKey { get; private set; }

    /// <summary>Lower-case hex SHA-256 of the image bytes (duplicate photo detection).</summary>
    public string ImageSha256 { get; }

    /// <summary>A known supplier whose normalized name matches <see cref="SupplierNameRaw"/>.</summary>
    public Guid? SupplierId { get; private set; }

    /// <summary>The supplier name as read from the invoice.</summary>
    public string? SupplierNameRaw { get; private set; }

    public string? InvoiceNumber { get; private set; }

    public DateOnly? InvoiceDate { get; private set; }

    /// <summary>The printed total payable, if any.</summary>
    public decimal? TotalAmount { get; private set; }

    /// <summary>Sum of the lines' totals once lines exist.</summary>
    public decimal? ComputedTotal { get; private set; }

    public IReadOnlyList<InvoiceWarning> Warnings { get; private set; } = [];

    /// <summary>The untrusted model output kept for audit (AGENTS.md rule 15). Always valid JSON. Never logged.</summary>
    public string? RawAiJson { get; private set; }

    public InvoiceFailureReason? FailureReason { get; private set; }

    public DateTimeOffset? ProcessedAt { get; private set; }

    public DateTimeOffset? ConfirmedAt { get; private set; }

    public Guid? ConfirmedByUserId { get; private set; }

    public DateTimeOffset CreatedAt { get; }

    /// <summary>
    /// When the image retention job recorded that the storage lifecycle rule has expired the image
    /// (docs/operations/image-lifecycle.md); <c>null</c> while the image is kept. Set in bulk by that job only.
    /// </summary>
    public DateTimeOffset? ImageDeletedAt { get; private set; }

    /// <summary>The lines, numbered from 1.</summary>
    public IReadOnlyCollection<InvoiceItem> Items => _items.AsReadOnly();

    /// <summary>
    /// A new scan waiting for the background job. The id exists before save (ADR-0010), so the image key can embed it:
    /// <paramref name="imageObjectKeyFor"/> builds the key from the new invoice's id.
    /// </summary>
    public static Result<Invoice> CreateForScan(
        Guid shopId, Guid clientRequestId, Guid userId, string imageSha256, DateTimeOffset now, Func<Guid, string> imageObjectKeyFor)
    {
        ArgumentNullException.ThrowIfNull(imageObjectKeyFor);

        var error = (shopId == Guid.Empty ? InvoiceErrors.ShopRequired : null)
            ?? (clientRequestId == Guid.Empty ? InvoiceErrors.ClientRequestIdRequired : null)
            ?? (userId == Guid.Empty ? InvoiceErrors.UserRequired : null)
            ?? (IsSha256Hex(imageSha256) ? null : InvoiceErrors.ImageHashInvalid);
        if (error is not null)
        {
            return error;
        }

        var invoice = new Invoice(shopId, clientRequestId, userId, string.Empty, imageSha256, now);
        invoice.ImageObjectKey = imageObjectKeyFor(invoice.Id);
        return string.IsNullOrWhiteSpace(invoice.ImageObjectKey) ? InvoiceErrors.ImageKeyRequired : invoice;
    }

    /// <summary>
    /// The job's successful result: header, 1..100 readable lines (numbered in order from 1) and the warnings. The status
    /// becomes <see cref="InvoiceStatus.NeedsReview"/> with or without warnings: a person reviews every invoice. On
    /// failure nothing changes.
    /// </summary>
    public Result CompleteExtraction(
        InvoiceHeader header,
        IReadOnlyList<InvoiceLineDraft> lines,
        IReadOnlyList<InvoiceWarning> warnings,
        string? rawAiJson,
        Guid? supplierId,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(header);
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentNullException.ThrowIfNull(warnings);

        var error = EnsureProcessing()
            ?? (lines.Count == 0 ? InvoiceErrors.LinesRequired : null)
            ?? (lines.Count > InvoiceLimits.MaxLines ? InvoiceErrors.TooManyLines : null)
            ?? (header.TotalAmount is { } total ? Precision.ValidateMoney(total, nameof(TotalAmount)) : null);
        if (error is not null)
        {
            return error;
        }

        var items = new List<InvoiceItem>(lines.Count);
        for (var index = 0; index < lines.Count; index++)
        {
            var item = InvoiceItem.FromDraft(ShopId, Id, index + 1, lines[index]);
            if (item.IsFailure)
            {
                return item.Error;
            }

            items.Add(item.Value);
        }

        var computedTotal = items.Sum(item => item.LineTotal);
        var totalError = Precision.ValidateMoney(computedTotal, nameof(ComputedTotal));
        if (totalError is not null)
        {
            return totalError;
        }

        _items.AddRange(items);
        SupplierId = supplierId;
        SupplierNameRaw = header.SupplierName;
        InvoiceNumber = header.InvoiceNumber;
        InvoiceDate = header.InvoiceDate;
        TotalAmount = header.TotalAmount;
        ComputedTotal = computedTotal;
        Warnings = warnings.Distinct().ToArray();
        RawAiJson = rawAiJson;
        ProcessedAt = now;
        Status = InvoiceStatus.NeedsReview;
        return Result.Success();
    }

    /// <summary>
    /// The review edit (contract I4, docs/design/backend.md §9.1): replaces the header, every line and the warnings while
    /// the invoice is <see cref="InvoiceStatus.NeedsReview"/>. A line keeps its match confidence only when its line number,
    /// product and unit equal the previous line with that number. The caller removes the previous lines from persistence
    /// (they are referenced by nothing before confirm). On failure nothing changes.
    /// </summary>
    public Result ReplaceLines(
        InvoiceHeader header, IReadOnlyList<InvoiceReviewLine> lines, Guid? supplierId, IReadOnlyList<InvoiceWarning> warnings)
    {
        ArgumentNullException.ThrowIfNull(header);
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentNullException.ThrowIfNull(warnings);

        var error = (Status == InvoiceStatus.NeedsReview ? null : InvoiceErrors.NotEditable)
            ?? (lines.Count == 0 ? InvoiceErrors.LinesRequired : null)
            ?? (lines.Count > InvoiceLimits.MaxLines ? InvoiceErrors.TooManyLines : null)
            ?? (lines.Select(line => line.LineNo).Distinct().Count() == lines.Count ? null : InvoiceErrors.LineNumberInvalid)
            ?? (header.TotalAmount is { } total ? Precision.ValidateMoney(total, nameof(TotalAmount)) : null);
        if (error is not null)
        {
            return error;
        }

        var previous = _items.ToDictionary(item => item.LineNo);
        var items = new List<InvoiceItem>(lines.Count);
        foreach (var line in lines.OrderBy(line => line.LineNo))
        {
            var item = InvoiceItem.FromReview(ShopId, Id, line, KeptConfidence(previous, line));
            if (item.IsFailure)
            {
                return item.Error;
            }

            items.Add(item.Value);
        }

        var computedTotal = items.Sum(item => item.LineTotal);
        var totalError = Precision.ValidateMoney(computedTotal, nameof(ComputedTotal));
        if (totalError is not null)
        {
            return totalError;
        }

        _items.Clear();
        _items.AddRange(items);
        SupplierId = supplierId;
        SupplierNameRaw = header.SupplierName;
        InvoiceNumber = header.InvoiceNumber;
        InvoiceDate = header.InvoiceDate;
        TotalAmount = header.TotalAmount;
        ComputedTotal = computedTotal;
        Warnings = warnings.Distinct().ToArray();
        return Result.Success();
    }

    /// <summary>Whether a line has neither an existing product nor a new one (confirm refuses it, contract I5).</summary>
    public bool HasUnresolvedLine => _items.Exists(item => item.ProductId is null && item.NewProductName is null);

    /// <summary>
    /// Confirm (contract I5, docs/design/backend.md §11 step 9, §10): status <see cref="InvoiceStatus.Confirmed"/>, the
    /// confirmation time and user, the supplier, and on every line <c>confirmed_at</c> and, for a new-product line, the
    /// product created from it (<paramref name="createdProductIdByLineId"/>, keyed by line id, exactly the new-product
    /// lines). Stock is received by <c>IStockService</c> in the same transaction, not here. On failure nothing changes.
    /// </summary>
    public Result Confirm(Guid userId, Guid? supplierId, IReadOnlyDictionary<Guid, Guid> createdProductIdByLineId, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(createdProductIdByLineId);

        var error = (Status == InvoiceStatus.NeedsReview ? null : InvoiceErrors.NotConfirmable)
            ?? (userId == Guid.Empty ? InvoiceErrors.UserRequired : null)
            ?? (HasUnresolvedLine ? InvoiceErrors.LineProductRequired : null)
            ?? (CreatedProductsMatch(createdProductIdByLineId) ? null : InvoiceErrors.CreatedProductsMismatch);
        if (error is not null)
        {
            return error;
        }

        foreach (var item in _items)
        {
            item.MarkConfirmed(now, createdProductIdByLineId.TryGetValue(item.Id, out var created) ? created : null);
        }

        SupplierId = supplierId;
        ConfirmedByUserId = userId;
        ConfirmedAt = now;
        Status = InvoiceStatus.Confirmed;
        return Result.Success();
    }

    /// <summary>Whether the current line <paramref name="lineNo"/> references <paramref name="productId"/> (unchanged product).</summary>
    public bool HasProductOnLine(int lineNo, Guid productId) =>
        _items.Any(item => item.LineNo == lineNo && item.ProductId == productId);

    /// <summary>
    /// The platform admin queues a <see cref="InvoiceStatus.Failed"/> invoice for processing again (contract AD19): it
    /// goes back to <see cref="InvoiceStatus.Processing"/> without its failure. The earlier raw AI output stays until the
    /// next run replaces it. Any other status fails with <see cref="InvoiceErrors.NotRerunnable"/>.
    /// </summary>
    public Result Rerun()
    {
        if (Status != InvoiceStatus.Failed)
        {
            return InvoiceErrors.NotRerunnable;
        }

        Status = InvoiceStatus.Processing;
        FailureReason = null;
        ProcessedAt = null;
        return Result.Success();
    }

    /// <summary>The job could not produce lines. Terminal for the user: they scan again (the admin may re-run it, AD19).</summary>
    public Result MarkFailed(InvoiceFailureReason reason, string? rawAiJson, DateTimeOffset now)
    {
        var error = EnsureProcessing();
        if (error is not null)
        {
            return error;
        }

        Status = InvoiceStatus.Failed;
        FailureReason = reason;
        RawAiJson = rawAiJson;
        ProcessedAt = now;
        return Result.Success();
    }

    private static decimal? KeptConfidence(Dictionary<int, InvoiceItem> previous, InvoiceReviewLine line) =>
        line.Product is { } product
        && previous.TryGetValue(line.LineNo, out var before)
        && before.ProductId == product.ProductId
        && before.ProductUnitId == product.ProductUnitId
            ? before.MatchConfidence
            : null;

    private bool CreatedProductsMatch(IReadOnlyDictionary<Guid, Guid> createdProductIdByLineId)
    {
        var newProductLines = _items.Where(item => item.NewProductName is not null).Select(item => item.Id).ToHashSet();
        return newProductLines.SetEquals(createdProductIdByLineId.Keys)
            && createdProductIdByLineId.Values.All(productId => productId != Guid.Empty);
    }

    private static bool IsSha256Hex(string? value) =>
        value is { Length: InvoiceLimits.ImageSha256Length } && value.All(c => char.IsAsciiDigit(c) || c is >= 'a' and <= 'f');

    private Error? EnsureProcessing() => Status == InvoiceStatus.Processing ? null : InvoiceErrors.NotProcessing;
}
