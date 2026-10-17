namespace Stockly.Domain.Invoices;

/// <summary>
/// Lifecycle of a scanned invoice (docs/design/backend.md §1.3). Stored as <c>smallint</c>; values are only appended.
/// <c>Processing → NeedsReview | Failed</c> (job), <c>NeedsReview → Confirmed</c> (confirm). Failed and Confirmed are terminal.
/// </summary>
public enum InvoiceStatus
{
    Processing = 1,
    NeedsReview = 2,
    Confirmed = 3,
    Failed = 4,
}
