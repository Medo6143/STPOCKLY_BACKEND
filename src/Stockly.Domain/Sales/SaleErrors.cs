using Stockly.Domain.Common;

namespace Stockly.Domain.Sales;

public static class SaleErrors
{
    /// <summary>No such sale in the current shop. Also returned for another shop's sale (AGENTS.md rule 10).</summary>
    public static readonly Error NotFound =
        Error.NotFound("Sale.NotFound", "The sale was not found.");

    /// <summary>Archived products cannot be sold (contract S1).</summary>
    public static readonly Error ArchivedProduct =
        Error.Validation("Sale.ArchivedProduct", "An archived product cannot be sold.");

    /// <summary>The line's unit belongs to another product (contract S1).</summary>
    public static readonly Error UnitNotOfProduct =
        Error.Validation("Sale.UnitNotOfProduct", "The unit does not belong to the line's product.");

    /// <summary>Internal, never returned: a concurrent first request with the same <c>clientRequestId</c> won the insert.</summary>
    public static readonly Error DuplicateClientRequest =
        Error.Conflict("Sale.DuplicateClientRequest", "A sale with this client request id already exists.");

    /// <summary>Internal guard (the request validator rejects it first).</summary>
    public static readonly Error ShopRequired =
        Error.Validation("Sale.ShopRequired", "A sale must belong to a shop.");

    /// <summary>Internal guard (the request validator rejects it first).</summary>
    public static readonly Error ClientRequestIdRequired =
        Error.Validation("Sale.ClientRequestIdRequired", "A sale needs a client request id.");

    /// <summary>Internal guard: a sale records the user who made it.</summary>
    public static readonly Error UserRequired =
        Error.Validation("Sale.UserRequired", "A sale needs the user who recorded it.");

    /// <summary>Internal guard (the request validator rejects it first).</summary>
    public static readonly Error NoteTooLong =
        Error.Validation("Sale.NoteTooLong", $"A sale note has at most {SaleLimits.NoteMaxLength} characters.");

    /// <summary>Internal guard (the request validator rejects it first).</summary>
    public static readonly Error TooManyItems =
        Error.Validation("Sale.TooManyItems", $"A sale has at most {SaleLimits.MaxItems} lines.");

    /// <summary>Internal guard (the request validator rejects it first).</summary>
    public static readonly Error QuantityNotPositive =
        Error.Validation("Sale.QuantityNotPositive", "The quantity of a sale line must be greater than zero.");
}
