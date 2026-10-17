using Microsoft.EntityFrameworkCore;
using Stockly.Application.Abstractions.Identity;
using Stockly.Application.Abstractions.Persistence;
using Stockly.Domain.Invoices;
using Stockly.Domain.Platform;
using Stockly.Infrastructure.Persistence.Configurations;

namespace Stockly.Infrastructure.Persistence.Repositories;

/// <summary>Invoice writes and decision lookups. Every query runs under the tenant filter (ADR-0002); indexes in docs/erd.md §3.3.</summary>
internal sealed class InvoiceRepository(StocklyDbContext db, ICurrentTenant currentTenant) : IInvoiceRepository
{
    /// <summary>Model name recorded for a reused extraction whose original usage row is gone.</summary>
    public const string UnknownModel = "unknown";

    private const string LockInvoiceSql =
        "SELECT 1 FROM " + InvoiceConfiguration.TableName + " WHERE id = {0} AND shop_id = {1} FOR UPDATE";

    public void Add(Invoice invoice) => db.Set<Invoice>().Add(invoice);

    public void RemoveItems(IReadOnlyCollection<InvoiceItem> items) => db.Set<InvoiceItem>().RemoveRange(items);

    // Primary key.
    public Task<InvoiceProcessingInfo?> FindProcessingInfoAsync(Guid invoiceId, CancellationToken cancellationToken) =>
        db.Set<Invoice>().AsNoTracking()
            .Where(invoice => invoice.Id == invoiceId)
            .Select(invoice => new InvoiceProcessingInfo(invoice.Status, invoice.ImageObjectKey, invoice.ImageSha256))
            .SingleOrDefaultAsync(cancellationToken);

    public async Task<Invoice?> FindForUpdateAsync(Guid invoiceId, CancellationToken cancellationToken)
    {
        if (db.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException("The invoice lock must be taken inside a transaction, or it is released at once.");
        }

        if (currentTenant.ShopId is not { } shopId)
        {
            return null;
        }

        // The lock is taken first (rule 4: the invoice before any product); the read that follows sees the latest row.
        await db.Database.ExecuteSqlRawAsync(LockInvoiceSql, [invoiceId, shopId], cancellationToken);
        return await db.Set<Invoice>()
            .Include(invoice => invoice.Items)
            .SingleOrDefaultAsync(invoice => invoice.Id == invoiceId, cancellationToken);
    }

    // ix_invoices_shop_id_image_sha256 finds the candidates; ix_ai_usage_records_invoice_id the model.
    public async Task<ReusableExtraction?> FindReusableExtractionAsync(
        string imageSha256, Guid excludingInvoiceId, CancellationToken cancellationToken)
    {
        var row = await db.Set<Invoice>().AsNoTracking()
            .Where(invoice => invoice.ImageSha256 == imageSha256
                && invoice.Id != excludingInvoiceId
                && (invoice.Status == InvoiceStatus.NeedsReview || invoice.Status == InvoiceStatus.Confirmed)
                && invoice.RawAiJson != null)
            .OrderByDescending(invoice => invoice.CreatedAt)
            .Select(invoice => new
            {
                invoice.RawAiJson,
                Model = db.Set<AiUsageRecord>()
                    .Where(record => record.InvoiceId == invoice.Id && record.Outcome == AiCallOutcome.Succeeded)
                    .OrderByDescending(record => record.CreatedAt)
                    .Select(record => record.Model)
                    .FirstOrDefault(),
            })
            .FirstOrDefaultAsync(cancellationToken);

        return row is null ? null : new ReusableExtraction(row.RawAiJson!, row.Model ?? UnknownModel);
    }

    // ix_invoices_shop_id_created_at.
    public Task<int> CountCreatedSinceAsync(DateTimeOffset since, CancellationToken cancellationToken) =>
        db.Set<Invoice>().AsNoTracking().CountAsync(invoice => invoice.CreatedAt >= since, cancellationToken);

    // ix_invoices_shop_id_supplier_id_invoice_number (shop_id, supplier_id, invoice_number) WHERE invoice_number IS NOT NULL.
    public Task<bool> ExistsOtherWithNumberAsync(
        Guid invoiceId, Guid? supplierId, string invoiceNumber, CancellationToken cancellationToken) =>
        db.Set<Invoice>().AsNoTracking().AnyAsync(
            invoice => invoice.Id != invoiceId
                && invoice.Status != InvoiceStatus.Failed
                && invoice.InvoiceNumber == invoiceNumber
                && invoice.SupplierId == supplierId,
            cancellationToken);
}
