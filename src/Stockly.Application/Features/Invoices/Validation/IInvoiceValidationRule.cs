using Stockly.Domain.Invoices;

namespace Stockly.Application.Features.Invoices.Validation;

/// <summary>
/// One invoice check that may raise a warning (docs/design/backend.md §7.3). A new check is a new class registered in
/// DI, never a new branch in a switch (SOLID O). Rules never change the invoice status.
/// </summary>
public interface IInvoiceValidationRule
{
    /// <summary>Rules run by ascending order; warnings are returned in that order.</summary>
    int Order { get; }

    /// <summary>The stages this rule runs at.</summary>
    InvoiceValidationStage Stages { get; }

    ValueTask<InvoiceWarning?> EvaluateAsync(InvoiceValidationContext context, CancellationToken cancellationToken);
}

/// <summary>When the rules run: right after extraction (job), or after a review edit (I4).</summary>
[Flags]
public enum InvoiceValidationStage
{
    Extraction = 1,
    Review = 2,
}

/// <summary>
/// What the rules see. <see cref="UnreadableLineCount"/> and <see cref="Truncated"/> are known only at extraction
/// (0 and <c>false</c> at review).
/// </summary>
public sealed record InvoiceValidationContext(
    InvoiceValidationStage Stage,
    Guid InvoiceId,
    Guid? SupplierId,
    string? InvoiceNumber,
    decimal? TotalAmount,
    decimal ComputedTotal,
    int LineCount,
    int UnreadableLineCount,
    bool Truncated);
