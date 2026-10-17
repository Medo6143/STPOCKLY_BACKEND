using Stockly.Domain.Invoices;

namespace Stockly.Application.Abstractions.Persistence;

/// <summary>Invoice writes and the lookups the scan and the job decide on. Every query runs under the tenant filter (ADR-0002).</summary>
public interface IInvoiceRepository
{
    void Add(Invoice invoice);

    /// <summary>
    /// Deletes these lines on the next save. The review edit replaces every line (docs/design/backend.md §9.1 step 5);
    /// nothing references a line before confirm.
    /// </summary>
    void RemoveItems(IReadOnlyCollection<InvoiceItem> items);

    /// <summary>What the job needs before it calls the AI, without tracking; <c>null</c> when missing or another shop's.</summary>
    Task<InvoiceProcessingInfo?> FindProcessingInfoAsync(Guid invoiceId, CancellationToken cancellationToken);

    /// <summary>
    /// The invoice, tracked, with its row locked (<c>SELECT ... FOR UPDATE</c>) until the transaction ends. Lock order:
    /// the invoice first (AGENTS.md rule 4). <c>null</c> when missing or another shop's.
    /// </summary>
    Task<Invoice?> FindForUpdateAsync(Guid invoiceId, CancellationToken cancellationToken);

    /// <summary>
    /// The model output of the shop's newest other invoice with the same image hash that was extracted
    /// (<c>NeedsReview</c> or <c>Confirmed</c>, with raw AI JSON), and the model that produced it; <c>null</c> when none.
    /// </summary>
    Task<ReusableExtraction?> FindReusableExtractionAsync(string imageSha256, Guid excludingInvoiceId, CancellationToken cancellationToken);

    /// <summary>Invoices of the shop created at or after <paramref name="since"/> (daily quota fallback when Redis is down).</summary>
    Task<int> CountCreatedSinceAsync(DateTimeOffset since, CancellationToken cancellationToken);

    /// <summary>
    /// Whether another invoice of the shop that did not fail has this exact invoice number and the same supplier
    /// (<c>null</c> matches an unknown supplier).
    /// </summary>
    Task<bool> ExistsOtherWithNumberAsync(Guid invoiceId, Guid? supplierId, string invoiceNumber, CancellationToken cancellationToken);
}

public sealed record InvoiceProcessingInfo(InvoiceStatus Status, string ImageObjectKey, string ImageSha256);

/// <summary>An earlier extraction of the same photo: its stored raw JSON and the model that produced it.</summary>
public sealed record ReusableExtraction(string RawAiJson, string Model);
