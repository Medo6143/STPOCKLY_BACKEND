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
}
