using System.Globalization;

namespace Stockly.Application.Features.Invoices;

/// <summary>
/// Object keys of invoice images (ADR-0008): <c>invoices/{shopId}/{yyyy}/{MM}/{invoiceId}.jpg</c>, year and month in UTC.
/// The extension stays <c>.jpg</c> for every type; the object's Content-Type holds the real one (backend.md §17.1 item 9).
/// </summary>
public static class InvoiceImageKeys
{
    public static string For(Guid shopId, Guid invoiceId, DateTimeOffset now) =>
        string.Create(CultureInfo.InvariantCulture, $"invoices/{shopId:D}/{now.UtcDateTime:yyyy}/{now.UtcDateTime:MM}/{invoiceId:D}.jpg");
}
