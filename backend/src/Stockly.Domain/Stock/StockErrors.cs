using Stockly.Domain.Common;

namespace Stockly.Domain.Stock;

public static class StockErrors
{
    public static readonly Error ZeroChange =
        Error.Validation("Stock.ZeroChange", "A stock movement must change the quantity.");

    public static readonly Error UserRequired =
        Error.Validation("Stock.UserRequired", "A stock movement must record the user who made it.");

    public static readonly Error UnknownReason =
        Error.Validation("Stock.UnknownReason", "The movement reason is not supported.");

    public static readonly Error InvoiceSourceRequired =
        Error.Validation("Stock.InvoiceSourceRequired", "This movement must reference an invoice and an invoice line.");

    public static readonly Error SaleSourceRequired =
        Error.Validation("Stock.SaleSourceRequired", "A sale movement must reference a sale and a sale line.");

    public static readonly Error SourceNotAllowed =
        Error.Validation("Stock.SourceNotAllowed", "This movement must not reference an invoice or a sale.");

    public static readonly Error EmptySourceReference =
        Error.Validation("Stock.EmptySourceReference", "Invoice and sale references must not be empty.");

    public static readonly Error ChangeMustBePositive =
        Error.Validation("Stock.ChangeMustBePositive", "This movement must increase stock.");

    public static readonly Error ChangeMustBeNegative =
        Error.Validation("Stock.ChangeMustBeNegative", "This movement must decrease stock.");

    public static readonly Error NoteRequired =
        Error.Validation("Stock.NoteRequired", "A manual adjustment needs a note explaining why.");
}
