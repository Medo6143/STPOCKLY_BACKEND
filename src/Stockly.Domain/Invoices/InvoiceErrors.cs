using Stockly.Domain.Common;

namespace Stockly.Domain.Invoices;

public static class InvoiceErrors
{
    /// <summary>No such invoice in the current shop. Also returned for another shop's invoice (AGENTS.md rule 10).</summary>
    public static readonly Error NotFound =
        Error.NotFound("Invoice.NotFound", "The invoice was not found.");

    public static readonly Error ImageRequired =
        Error.Validation("Invoice.ImageRequired", "An invoice image is required.");

    public static readonly Error UnsupportedImageType =
        Error.Validation("Invoice.UnsupportedImageType", "The image must be a JPEG, PNG or WebP file.");

    public static readonly Error ImageTooLarge =
        Error.PayloadTooLarge("Invoice.ImageTooLarge", "The image is larger than 8 MB.");

    /// <summary>Internal, never returned: a concurrent first request with the same <c>clientRequestId</c> won the insert.</summary>
    public static readonly Error DuplicateClientRequest =
        Error.Conflict("Invoice.DuplicateClientRequest", "An invoice with this client request id already exists.");

    /// <summary>Internal guard: only a <see cref="InvoiceStatus.Processing"/> invoice receives the job's result.</summary>
    public static readonly Error NotProcessing =
        Error.Conflict("Invoice.NotProcessing", "The invoice is no longer being processed.");

    public static readonly Error ShopRequired =
        Error.Validation("Invoice.ShopRequired", "An invoice must belong to a shop.");

    public static readonly Error ClientRequestIdRequired =
        Error.Validation("Invoice.ClientRequestIdRequired", "An invoice needs the client request id of its scan.");

    public static readonly Error UserRequired =
        Error.Validation("Invoice.UserRequired", "An invoice needs the user who scanned it.");

    public static readonly Error ImageKeyRequired =
        Error.Validation("Invoice.ImageKeyRequired", "An invoice needs the storage key of its image.");

    public static readonly Error ImageHashInvalid =
        Error.Validation("Invoice.ImageHashInvalid", "The image hash must be 64 lower-case hex characters.");

    public static readonly Error LinesRequired =
        Error.Validation("Invoice.LinesRequired", "An extracted invoice needs at least one line.");

    public static readonly Error TooManyLines =
        Error.Validation("Invoice.TooManyLines", $"An invoice has at most {InvoiceLimits.MaxLines} lines.");

    public static readonly Error LineNameRequired =
        Error.Validation("Invoice.LineNameRequired", "Every invoice line needs a product name.");

    public static readonly Error LineQuantityNotPositive =
        Error.Validation("Invoice.LineQuantityNotPositive", "The quantity of an invoice line must be greater than zero.");

    /// <summary>The review edit (I4) needs an invoice waiting for review.</summary>
    public static readonly Error NotEditable =
        Error.Conflict("Invoice.NotEditable", "Only an invoice waiting for review can be edited.");

    /// <summary>A reviewed line has neither an existing product nor a new one (I4, I5).</summary>
    public static readonly Error LineProductRequired =
        Error.Validation("Invoice.LineProductRequired", "Every invoice line needs a product or a new product.");

    /// <summary>The line's unit belongs to another product (I4).</summary>
    public static readonly Error UnitNotOfProduct =
        Error.Validation("Invoice.UnitNotOfProduct", "The unit does not belong to the line's product.");

    /// <summary>Internal guard (the request validator rejects it first): a line has both a product and a new product.</summary>
    public static readonly Error LineProductConflict =
        Error.Validation("Invoice.LineProductConflict", "An invoice line has either a product or a new product, not both.");

    /// <summary>Internal guard (the request validator rejects it first): line numbers are 1..100 and unique.</summary>
    public static readonly Error LineNumberInvalid =
        Error.Validation("Invoice.LineNumberInvalid", $"Line numbers are unique and between 1 and {InvoiceLimits.MaxLines}.");

    /// <summary>Internal guard: a match confidence is 0.00–1.00 with at most 2 decimals.</summary>
    public static readonly Error MatchConfidenceInvalid =
        Error.Validation("Invoice.MatchConfidenceInvalid", "A match confidence is between 0 and 1 with at most 2 decimals.");

    /// <summary>Internal guard (the request validator rejects it first): a new product needs a name and a base unit name.</summary>
    public static readonly Error NewProductIncomplete =
        Error.Validation("Invoice.NewProductIncomplete", "A new product needs a name and a base unit name.");

    /// <summary>Confirm (I5) needs an invoice waiting for review; a processing or failed invoice cannot be confirmed.</summary>
    public static readonly Error NotConfirmable =
        Error.Conflict("Invoice.NotConfirmable", "Only an invoice waiting for review can be confirmed.");

    /// <summary>
    /// Internal, never returned: the unique index on (invoice_item_id, reason) refused a second receipt, so a concurrent
    /// confirm already received this invoice (the last safety net, docs/design/backend.md §11 step 10).
    /// </summary>
    public static readonly Error AlreadyConfirmed =
        Error.Conflict("Invoice.AlreadyConfirmed", "The invoice was already confirmed.");

    /// <summary>Only a <see cref="InvoiceStatus.Failed"/> invoice can be processed again by the platform admin (AD19).</summary>
    public static readonly Error NotRerunnable =
        Error.Conflict("Invoice.NotRerunnable", "Only a failed invoice can be processed again.");

    /// <summary>Internal guard: confirm names a created product for exactly the new-product lines.</summary>
    public static readonly Error CreatedProductsMismatch =
        Error.Validation("Invoice.CreatedProductsMismatch", "Every new-product line, and only those, needs its created product.");
}
