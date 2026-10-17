using System.Globalization;
using Stockly.Application.Abstractions.Ai;
using Stockly.Application.Features.Invoices.Matching;
using Stockly.Domain.Common;
using Stockly.Domain.Invoices;
using Stockly.Domain.Text;

namespace Stockly.Application.Features.Invoices.ProcessInvoice;

/// <summary>
/// Turns a validated <see cref="ExtractedInvoice"/> into typed values (docs/design/backend.md §7.2). Pure. Nothing is
/// rounded: a value with too many decimals is unreadable, not rounded (erd §1). A line is readable when its normalized
/// name is not empty, its quantity is &gt; 0 with at most 3 decimals, and its unit price has at most 2 decimals (or can be
/// derived exactly from <c>lineTotal / quantity</c>). Unreadable lines are counted, not kept.
/// </summary>
public static class ExtractedInvoiceNormalizer
{
    private const string DateFormat = "yyyy-MM-dd";
    private const string Field = "ai";

    public static NormalizedInvoice Normalize(ExtractedInvoice extraction)
    {
        ArgumentNullException.ThrowIfNull(extraction);

        var header = new InvoiceHeader(
            OptionalText.Clean(extraction.SupplierName),
            OptionalText.Clean(extraction.InvoiceNumber),
            ParseDate(extraction.InvoiceDate),
            ParseMoney(extraction.TotalAmount));

        var lines = new List<InvoiceLineDraft>();
        var matchInputs = new List<MatchInput>();
        var unreadable = 0;
        foreach (var line in extraction.Items)
        {
            if (ToDraft(line) is { } draft)
            {
                lines.Add(draft);
                matchInputs.Add(new MatchInput(lines.Count, ArabicTextNormalizer.Normalize(draft.RawName), NormalizeUnit(line.Unit)));
            }
            else
            {
                unreadable++;
            }
        }

        return new NormalizedInvoice(header, lines, matchInputs, unreadable, extraction.Truncated);
    }

    private static string? NormalizeUnit(string? unit) =>
        OptionalText.Clean(unit) is { } text && ArabicTextNormalizer.Normalize(text) is { Length: > 0 } normalized ? normalized : null;

    private static InvoiceLineDraft? ToDraft(ExtractedInvoiceLine line)
    {
        var name = OptionalText.Clean(line.Name);
        var quantity = ParseDecimal(line.Quantity);
        if (name is null || ArabicTextNormalizer.Normalize(name).Length == 0
            || quantity is not > 0m || Precision.ValidateQuantity(quantity.Value, Field) is not null)
        {
            return null;
        }

        var unitPrice = line.UnitPrice is null ? DeriveUnitPrice(line.LineTotal, quantity.Value) : ParseMoney(line.UnitPrice);
        if (unitPrice is not { } price || Precision.ValidateMoney(InvoiceItem.ComputeLineTotal(quantity.Value, price), Field) is not null)
        {
            return null;
        }

        return new InvoiceLineDraft(name, quantity.Value, price, ParseDate(line.ExpiryDate), OptionalText.Clean(line.BatchNo));
    }

    // Only an exact 2-decimal result is used; the model is told never to calculate, so we do not guess either.
    private static decimal? DeriveUnitPrice(string? lineTotal, decimal quantity)
    {
        if (ParseMoney(lineTotal) is not { } total)
        {
            return null;
        }

        var price = total / quantity;
        return decimal.Round(price, Precision.MoneyDecimals) == price ? price : null;
    }

    private static decimal? ParseMoney(string? text) =>
        ParseDecimal(text) is { } value && Precision.ValidateMoney(value, Field) is null ? value : null;

    private static decimal? ParseDecimal(string? text) =>
        text is not null && decimal.TryParse(text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;

    private static DateOnly? ParseDate(string? text) =>
        text is not null && DateOnly.TryParseExact(text, DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date
            : null;
}

/// <summary>
/// The typed header, the readable lines in order, what matching needs for each of them (line number from 1, normalized
/// name and unit text), how many lines were unreadable, and whether the model hit the line cap.
/// </summary>
public sealed record NormalizedInvoice(
    InvoiceHeader Header,
    IReadOnlyList<InvoiceLineDraft> Lines,
    IReadOnlyList<MatchInput> MatchInputs,
    int UnreadableLineCount,
    bool Truncated)
{
    /// <summary>Σ line totals, as the invoice will compute them.</summary>
    public decimal ComputedTotal => Lines.Sum(line => InvoiceItem.ComputeLineTotal(line.Quantity, line.UnitPrice));
}
