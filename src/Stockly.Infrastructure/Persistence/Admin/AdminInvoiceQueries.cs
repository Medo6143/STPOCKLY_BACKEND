using Microsoft.EntityFrameworkCore;
using Stockly.Application.Abstractions.Admin;
using Stockly.Application.Features.Admin;
using Stockly.Contracts.Admin;
using Stockly.Contracts.Common;
using Stockly.Domain.Invoices;
using Stockly.Domain.Platform;
using Stockly.Domain.Products;
using Stockly.Infrastructure.Persistence.Configurations;
using Stockly.Infrastructure.Persistence.Queries;

namespace Stockly.Infrastructure.Persistence.Admin;

/// <summary>Cross-tenant invoice access of the platform admin (AD17–AD19, ADR-0005 update 2026-10-18). Indexes in docs/erd.md §3.3, §3.6.</summary>
internal sealed class AdminInvoiceQueries(StocklyDbContext db) : IAdminInvoiceQueries
{
    private const string LockInvoiceSql = "SELECT 1 FROM " + InvoiceConfiguration.TableName + " WHERE id = {0} FOR UPDATE";

    // Every shop: ix_invoices_status_created_at_failed (status, created_at DESC) WHERE status = Failed. One shop:
    // ix_invoices_shop_id_status_created_at. Per row: the shop name (PK) and the AI calls (ix_ai_usage_records_invoice_id).
    public async Task<PagedResponse<AdminFailedInvoice>> ListFailedAsync(
        Guid? shopId, int page, int pageSize, CancellationToken cancellationToken)
    {
        var invoices = db.Set<Invoice>().IgnoreQueryFilters().AsNoTracking().Where(invoice => invoice.Status == InvoiceStatus.Failed);
        if (shopId is { } shop)
        {
            invoices = invoices.Where(invoice => invoice.ShopId == shop);
        }

        var totalCount = await invoices.CountAsync(cancellationToken);
        var rows = await invoices
            .OrderByDescending(invoice => invoice.CreatedAt)
            .ThenByDescending(invoice => invoice.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(invoice => new
            {
                invoice.Id,
                invoice.ShopId,
                ShopName = db.Shops.Where(candidate => candidate.Id == invoice.ShopId).Select(candidate => candidate.Name).FirstOrDefault(),
                invoice.FailureReason,
                Attempts = db.Set<AiUsageRecord>().IgnoreQueryFilters()
                    .Count(record => record.InvoiceId == invoice.Id && !record.CacheHit),
                invoice.CreatedAt,
                invoice.ProcessedAt,
            })
            .ToListAsync(cancellationToken);

        var items = rows
            .Select(row => new AdminFailedInvoice(
                row.Id,
                row.ShopId,
                row.ShopName ?? string.Empty,
                row.FailureReason?.ToString(),
                row.Attempts,
                row.CreatedAt.UtcDateTime,
                (row.ProcessedAt ?? row.CreatedAt).UtcDateTime))
            .ToList();
        return new PagedResponse<AdminFailedInvoice>(items, page, pageSize, totalCount);
    }

    // The I3 projection without the tenant filter, then the admin fields (PK) and the usage rows (ix_ai_usage_records_invoice_id).
    public async Task<AdminInvoiceRecord?> FindAsync(Guid invoiceId, CancellationToken cancellationToken)
    {
        var detail = await InvoiceDetailProjection.FindAsync(
            db.Set<Invoice>().IgnoreQueryFilters().AsNoTracking(),
            db.Products.IgnoreQueryFilters().AsNoTracking(),
            db.Set<ProductUnit>().IgnoreQueryFilters().AsNoTracking(),
            invoiceId,
            cancellationToken);
        if (detail is null)
        {
            return null;
        }

        var meta = await db.Set<Invoice>().IgnoreQueryFilters().AsNoTracking()
            .Where(invoice => invoice.Id == invoiceId)
            .Select(invoice => new
            {
                invoice.ShopId,
                ShopName = db.Shops.Where(shop => shop.Id == invoice.ShopId).Select(shop => shop.Name).FirstOrDefault(),
                invoice.ImageObjectKey,
                invoice.ImageDeletedAt,
                invoice.RawAiJson,
            })
            .SingleAsync(cancellationToken);

        var usage = await db.Set<AiUsageRecord>().IgnoreQueryFilters().AsNoTracking()
            .Where(record => record.InvoiceId == invoiceId)
            .OrderBy(record => record.CreatedAt)
            .ThenBy(record => record.Id)
            .Select(record => new
            {
                record.Id,
                record.Model,
                record.CacheHit,
                record.Outcome,
                record.PromptTokens,
                record.CompletionTokens,
                record.CostUsd,
                record.LatencyMs,
                record.CreatedAt,
            })
            .ToListAsync(cancellationToken);

        return new AdminInvoiceRecord(
            detail,
            meta.ShopId,
            meta.ShopName ?? string.Empty,
            meta.ImageObjectKey,
            meta.ImageDeletedAt is not null,
            meta.RawAiJson,
            [.. usage.Select(record => new AdminAiUsageDto(
                record.Id,
                record.Model,
                record.CacheHit,
                record.Outcome == AiCallOutcome.Succeeded,
                record.PromptTokens,
                record.CompletionTokens,
                AdminMapping.Usd(record.CostUsd),
                record.LatencyMs,
                record.CreatedAt.UtcDateTime))]);
    }

    // The row lock first (primary key), then the tracked read that sees the latest row. The lines are not needed.
    public async Task<Invoice?> FindForUpdateAsync(Guid invoiceId, CancellationToken cancellationToken)
    {
        if (db.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException("The invoice lock must be taken inside a transaction, or it is released at once.");
        }

        await db.Database.ExecuteSqlRawAsync(LockInvoiceSql, [invoiceId], cancellationToken);
        return await db.Set<Invoice>().IgnoreQueryFilters().SingleOrDefaultAsync(invoice => invoice.Id == invoiceId, cancellationToken);
    }
}
