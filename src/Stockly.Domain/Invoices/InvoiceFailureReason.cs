namespace Stockly.Domain.Invoices;

/// <summary>
/// Why processing failed. Written to <c>invoices.failure_reason</c> as its name (docs/design/backend.md §17.1 item 14);
/// the app shows a generic message and does not switch on these values.
/// </summary>
public enum InvoiceFailureReason
{
    /// <summary>The uploaded image is no longer in storage.</summary>
    ImageMissing = 1,

    /// <summary>No model answered (transport errors on both models, or a key, credit or permission error).</summary>
    AiUnavailable = 2,

    /// <summary>Models answered, but no output passed parsing and validation.</summary>
    AiOutputInvalid = 3,

    /// <summary>A valid output had no readable line.</summary>
    AiNoItems = 4,

    /// <summary>The job failed for another reason after all its retries.</summary>
    Unexpected = 5,
}
