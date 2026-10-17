namespace Stockly.Domain.Invoices;

/// <summary>
/// A review hint on an extracted invoice (contract §6). Stored in <c>invoices.warnings</c> as a jsonb array of these
/// names. Warnings never fail an invoice and never change its status.
/// </summary>
public enum InvoiceWarning
{
    /// <summary>The printed total differs from the sum of the lines by more than 2% (AGENTS.md rule 16).</summary>
    TotalMismatch = 1,

    /// <summary>The model hit the 100-line cap; later lines are missing.</summary>
    ItemCapExceeded = 2,

    /// <summary>Some lines could not be read; the user adds them in review.</summary>
    UnreadableLines = 3,

    /// <summary>Another invoice of the shop has the same number and supplier.</summary>
    DuplicateInvoiceNumber = 4,
}
