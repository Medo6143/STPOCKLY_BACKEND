using System.Diagnostics;
using System.Text.Json;
using FluentValidation;
using MediatR;
using Stockly.Application.Abstractions.Ai;
using Stockly.Application.Abstractions.Caching;
using Stockly.Application.Abstractions.Identity;
using Stockly.Application.Abstractions.Persistence;
using Stockly.Application.Abstractions.Storage;
using Stockly.Application.Abstractions.Time;
using Stockly.Application.Features.Invoices.Validation;
using Stockly.Domain.Common;
using Stockly.Domain.Invoices;
using Stockly.Domain.Platform;
using Stockly.Domain.Text;

namespace Stockly.Application.Features.Invoices.ProcessInvoice;

/// <summary>
/// docs/design/backend.md §4.2 with the §17.1 order: image → earlier extraction of the same photo (database) → AI result
/// cache (Redis) → OpenRouter → normalize → supplier → warnings → one locked save. Every AI call and every cache hit
/// becomes an <see cref="AiUsageRecord"/> (rule 18). Nothing here changes stock (rule 19).
/// </summary>
public sealed class ProcessInvoiceCommandHandler(
    ICurrentTenant currentTenant,
    IInvoiceRepository invoices,
    ISupplierRepository suppliers,
    IAiUsageRecordRepository usageRecords,
    IFileStorage storage,
    IAiResultCache aiCache,
    IAiInvoiceExtractor extractor,
    IValidator<ExtractedInvoice> outputValidator,
    InvoiceValidator invoiceValidator,
    IInvoiceStatusCache statusCache,
    IUnitOfWork unitOfWork,
    IClock clock) : IRequestHandler<ProcessInvoiceCommand, Result<ProcessInvoiceOutcome>>
{
    public async Task<Result<ProcessInvoiceOutcome>> Handle(ProcessInvoiceCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (currentTenant.ShopId is not { } shopId)
        {
            return InvoiceErrors.NotFound;
        }

        var info = await invoices.FindProcessingInfoAsync(request.InvoiceId, cancellationToken);
        if (info is null)
        {
            return InvoiceErrors.NotFound;
        }

        if (info.Status != InvoiceStatus.Processing)
        {
            return InvoiceErrors.NotProcessing;
        }

        var image = await storage.GetAsync(info.ImageObjectKey, cancellationToken);
        if (image is null)
        {
            // No AI call happened, so there is no usage row.
            return await SaveAsync(
                shopId, request.InvoiceId, [], invoice => invoice.MarkFailed(InvoiceFailureReason.ImageMissing, null, clock.UtcNow), cancellationToken);
        }

        var extracted = await ExtractAsync(shopId, request.InvoiceId, info.ImageSha256, image, cancellationToken);
        if (extracted.Extraction is null)
        {
            var reason = extracted.Failure ?? InvoiceFailureReason.AiOutputInvalid;
            return await SaveAsync(
                shopId, request.InvoiceId, extracted.Usage, invoice => invoice.MarkFailed(reason, extracted.RawJson, clock.UtcNow), cancellationToken);
        }

        var normalized = ExtractedInvoiceNormalizer.Normalize(extracted.Extraction);
        if (normalized.Lines.Count == 0)
        {
            return await SaveAsync(
                shopId,
                request.InvoiceId,
                extracted.Usage,
                invoice => invoice.MarkFailed(InvoiceFailureReason.AiNoItems, extracted.RawJson, clock.UtcNow),
                cancellationToken);
        }

        var header = normalized.Header;
        var supplierId = header.SupplierName is { } supplierName
            ? await suppliers.FindIdByNormalizedNameAsync(ArabicTextNormalizer.Normalize(supplierName), cancellationToken)
            : null;
        var warnings = await invoiceValidator.ValidateAsync(
            new InvoiceValidationContext(
                InvoiceValidationStage.Extraction,
                request.InvoiceId,
                supplierId,
                header.InvoiceNumber,
                header.TotalAmount,
                normalized.ComputedTotal,
                normalized.Lines.Count,
                normalized.UnreadableLineCount,
                normalized.Truncated),
            cancellationToken);

        return await SaveAsync(
            shopId,
            request.InvoiceId,
            extracted.Usage,
            invoice =>
            {
                var completed = invoice.CompleteExtraction(header, normalized.Lines, warnings, extracted.RawJson, supplierId, clock.UtcNow);

                // A guard of the aggregate refused values the normalizer let through: the output is not usable.
                return completed.IsFailure && completed.Error != InvoiceErrors.NotProcessing
                    ? invoice.MarkFailed(InvoiceFailureReason.AiOutputInvalid, extracted.RawJson, clock.UtcNow)
                    : completed;
            },
            cancellationToken);
    }

    private async Task<Extracted> ExtractAsync(
        Guid shopId, Guid invoiceId, string imageSha256, StoredFile image, CancellationToken cancellationToken)
    {
        var started = Stopwatch.GetTimestamp();

        var reusable = await invoices.FindReusableExtractionAsync(imageSha256, invoiceId, cancellationToken);
        if (reusable is not null && TryParseUsable(reusable.RawAiJson) is { } reused)
        {
            return CacheHit(shopId, invoiceId, reusable.Model, reused, reusable.RawAiJson, started);
        }

        var cached = await aiCache.GetAsync(shopId, imageSha256, cancellationToken);
        if (cached is not null && IsUsable(cached.Extraction))
        {
            return CacheHit(shopId, invoiceId, cached.Model, cached.Extraction, AiJson.Serialize(cached.Extraction), started);
        }

        var result = await extractor.ExtractAsync(new AiExtractionRequest(image.Content, image.ContentType), cancellationToken);
        var now = clock.UtcNow;
        var usage = result.Attempts
            .Select(attempt => AiUsageRecord.Create(
                shopId,
                invoiceId,
                attempt.Model,
                attempt.PromptTokens,
                attempt.CompletionTokens,
                attempt.CostUsd,
                attempt.LatencyMs,
                attempt.Outcome,
                cacheHit: false,
                now))
            .ToList();

        if (result is { Extraction: { } extraction, Model: { } model })
        {
            // Cached before the database is touched: a Hangfire retry after a database failure does not pay again.
            await aiCache.SetAsync(shopId, imageSha256, model, extraction, cancellationToken);
            return new Extracted(extraction, null, RawAiJson.From(result.RawContent), usage);
        }

        return new Extracted(null, result.Failure ?? InvoiceFailureReason.AiUnavailable, RawAiJson.From(result.RawContent), usage);
    }

    private Extracted CacheHit(Guid shopId, Guid invoiceId, string model, ExtractedInvoice extraction, string rawJson, long started)
    {
        var latency = (int)Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        var record = AiUsageRecord.Create(
            shopId, invoiceId, model, 0, 0, 0m, latency, AiCallOutcome.Succeeded, cacheHit: true, clock.UtcNow);
        return new Extracted(extraction, null, RawAiJson.From(rawJson), [record]);
    }

    // Earlier output is untrusted too: it is parsed and validated again before reuse.
    private ExtractedInvoice? TryParseUsable(string rawJson)
    {
        try
        {
            var extraction = AiJson.Parse(rawJson);
            return IsUsable(extraction) ? extraction : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private bool IsUsable(ExtractedInvoice extraction) =>
        outputValidator.Validate(extraction).IsValid && extraction.Items.Count > 0;

    // One transaction: lock the invoice, re-check it is still processing, apply the outcome, add the usage rows. The
    // usage rows are kept even when another run finished the invoice first: the calls happened (rule 18).
    private async Task<Result<ProcessInvoiceOutcome>> SaveAsync(
        Guid shopId, Guid invoiceId, IReadOnlyList<AiUsageRecord> usage, Func<Invoice, Result> apply, CancellationToken cancellationToken)
    {
        var save = await unitOfWork.ExecuteInTransactionAsync(
            async token =>
            {
                foreach (var record in usage)
                {
                    usageRecords.Add(record);
                }

                var invoice = await invoices.FindForUpdateAsync(invoiceId, token);
                var applied = invoice is null ? InvoiceErrors.NotFound : apply(invoice);
                await unitOfWork.SaveChangesAsync(token);
                return applied.IsSuccess
                    ? new SaveOutcome(Outcome(invoice!, usage.Count), invoice)
                    : new SaveOutcome(applied.Error, null);
            },
            _ => true,
            cancellationToken);

        if (save.Changed is not null)
        {
            await statusCache.SetAsync(shopId, save.Changed.ToStatusSnapshot(), cancellationToken);
        }

        return save.Result;
    }

    private static ProcessInvoiceOutcome Outcome(Invoice invoice, int usageCount) =>
        new(invoice.Status, invoice.FailureReason, invoice.Warnings, invoice.Items.Count, usageCount);

    private sealed record SaveOutcome(Result<ProcessInvoiceOutcome> Result, Invoice? Changed);

    private sealed record Extracted(
        ExtractedInvoice? Extraction, InvoiceFailureReason? Failure, string? RawJson, IReadOnlyList<AiUsageRecord> Usage);
}
