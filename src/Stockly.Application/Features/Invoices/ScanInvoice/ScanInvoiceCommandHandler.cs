using System.Buffers;
using System.Security.Cryptography;
using MediatR;
using Stockly.Application.Abstractions.Caching;
using Stockly.Application.Abstractions.Identity;
using Stockly.Application.Abstractions.Jobs;
using Stockly.Application.Abstractions.Limits;
using Stockly.Application.Abstractions.Persistence;
using Stockly.Application.Abstractions.Storage;
using Stockly.Application.Abstractions.Time;
using Stockly.Application.Features.Auth;
using Stockly.Contracts.Invoices;
using Stockly.Domain.Common;
using Stockly.Domain.Invoices;
using ContractInvoiceStatus = Stockly.Contracts.Invoices.InvoiceStatus;

namespace Stockly.Application.Features.Invoices.ScanInvoice;

/// <summary>
/// docs/design/backend.md §3.2: image checks → idempotency (ADR-0006) → rate limit → daily quota → upload → insert →
/// enqueue → status cache. A repeat never consumes the quota or the rate limit. The AI is never called here (rule 14).
/// </summary>
public sealed class ScanInvoiceCommandHandler(
    ICurrentUser currentUser,
    ICurrentTenant currentTenant,
    IInvoiceRepository invoices,
    IInvoiceQueries queries,
    IShopPlanQueries plans,
    IScanRateLimiter rateLimiter,
    IScanQuotaCounter quota,
    IFileStorage storage,
    IBackgroundJobScheduler jobs,
    IInvoiceStatusCache statusCache,
    IUnitOfWork unitOfWork,
    IClock clock,
    ScanOptions options) : IRequestHandler<ScanInvoiceCommand, Result<ScanInvoiceResult>>
{
    public async Task<Result<ScanInvoiceResult>> Handle(ScanInvoiceCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (currentUser.UserId is not { } userId || currentTenant.ShopId is not { } shopId)
        {
            return AuthErrors.Unauthenticated;
        }

        if (request.Image is not { Length: > 0 } image)
        {
            return InvoiceErrors.ImageRequired;
        }

        if (image.Length > ScanLimits.MaxImageBytes)
        {
            return InvoiceErrors.ImageTooLarge;
        }

        var buffer = ArrayPool<byte>.Shared.Rent((int)image.Length);
        try
        {
            var read = await image.Content.ReadAtLeastAsync(
                buffer.AsMemory(0, (int)image.Length), (int)image.Length, throwOnEndOfStream: false, cancellationToken);
            var bytes = buffer.AsMemory(0, read);
            var contentType = InvoiceImageFormat.Detect(bytes.Span, image.ContentType);
            if (contentType is null)
            {
                return InvoiceErrors.UnsupportedImageType;
            }

            var clientRequestId = Guid.Parse(request.ClientRequestId!);
            var existing = await queries.FindListItemByClientRequestIdAsync(clientRequestId, cancellationToken);
            if (existing is not null)
            {
                return Repeat(shopId, existing);
            }

            return await ScanNewAsync(shopId, userId, clientRequestId, bytes, contentType, cancellationToken);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private async Task<Result<ScanInvoiceResult>> ScanNewAsync(
        Guid shopId, Guid userId, Guid clientRequestId, ReadOnlyMemory<byte> bytes, string contentType, CancellationToken cancellationToken)
    {
        // Rate limit before quota: a rate-limited request never burns quota.
        var rate = await rateLimiter.TryAcquireAsync(shopId, cancellationToken);
        if (!rate.Allowed)
        {
            return ScanErrors.RateLimited(TimeSpan.FromSeconds(rate.RetryAfterSeconds));
        }

        var dailyQuota = await plans.GetDailyScanQuotaAsync(cancellationToken) ?? 0;
        var consumed = await quota.TryConsumeAsync(shopId, dailyQuota, cancellationToken);
        var countedInRedis = consumed != ScanQuotaResult.Unavailable;
        if (!countedInRedis)
        {
            var usedToday = await invoices.CountCreatedSinceAsync(clock.CairoDayStartUtc(), cancellationToken);
            consumed = usedToday < dailyQuota ? ScanQuotaResult.Allowed : ScanQuotaResult.Exceeded;
        }

        if (consumed == ScanQuotaResult.Exceeded)
        {
            return ScanErrors.DailyQuotaExceeded(clock.UntilNextCairoMidnight());
        }

        var now = clock.UtcNow;
        var sha256 = Convert.ToHexStringLower(SHA256.HashData(bytes.Span));
        var invoice = Invoice.CreateForScan(
            shopId, clientRequestId, userId, sha256, now, invoiceId => InvoiceImageKeys.For(shopId, invoiceId, now));
        if (invoice.IsFailure)
        {
            await ReleaseAsync(shopId, countedInRedis, cancellationToken);
            return invoice.Error;
        }

        var created = invoice.Value;
        try
        {
            // Upload first: no database transaction stays open during the upload. An orphan object left by a failed
            // insert is removed by the bucket's lifecycle rule.
            await storage.PutAsync(created.ImageObjectKey, bytes, contentType, cancellationToken);
        }
        catch
        {
            await ReleaseAsync(shopId, countedInRedis, CancellationToken.None);
            throw;
        }

        invoices.Add(created);
        var saved = await unitOfWork.TrySaveChangesAsync(cancellationToken);
        if (saved.IsFailure)
        {
            return await LoseRaceAsync(shopId, clientRequestId, created, countedInRedis, saved.Error, cancellationToken);
        }

        // Cached before the job is queued: a fast job's status must not be overwritten by this Processing.
        await statusCache.SetAsync(shopId, created.ToStatusSnapshot(), cancellationToken);
        jobs.EnqueueProcessInvoice(shopId, created.Id);
        return new ScanInvoiceResult(created.ToListItem(), Created: true);
    }

    // A concurrent first request with the same clientRequestId inserted first: give back what this one took and
    // return the winner (docs/design/backend.md §3.2 step 10).
    private async Task<Result<ScanInvoiceResult>> LoseRaceAsync(
        Guid shopId, Guid clientRequestId, Invoice loser, bool countedInRedis, Error error, CancellationToken cancellationToken)
    {
        if (error != InvoiceErrors.DuplicateClientRequest)
        {
            return error;
        }

        await ReleaseAsync(shopId, countedInRedis, cancellationToken);
        await storage.DeleteAsync(loser.ImageObjectKey, cancellationToken);
        var winner = await queries.FindListItemByClientRequestIdAsync(clientRequestId, cancellationToken)
            ?? throw new InvalidOperationException("The invoice that won the client request id was not found.");
        return new ScanInvoiceResult(winner, Created: false);
    }

    // A repeat returns the existing invoice. One stuck in Processing longer than the worst-case job had its enqueue
    // lost (crash between commit and enqueue): queue it again; the job is idempotent.
    private ScanInvoiceResult Repeat(Guid shopId, InvoiceListItem existing)
    {
        var age = clock.UtcNow - new DateTimeOffset(existing.CreatedAt, TimeSpan.Zero);
        if (existing.Status == ContractInvoiceStatus.Processing && age > TimeSpan.FromSeconds(options.RequeueAfterSeconds))
        {
            jobs.EnqueueProcessInvoice(shopId, existing.Id);
        }

        return new ScanInvoiceResult(existing, Created: false);
    }

    private Task ReleaseAsync(Guid shopId, bool countedInRedis, CancellationToken cancellationToken) =>
        countedInRedis ? quota.ReleaseAsync(shopId, cancellationToken) : Task.CompletedTask;
}
