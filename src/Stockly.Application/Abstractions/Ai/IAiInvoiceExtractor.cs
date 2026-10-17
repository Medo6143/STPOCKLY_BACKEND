using Stockly.Domain.Invoices;
using Stockly.Domain.Platform;

namespace Stockly.Application.Abstractions.Ai;

/// <summary>
/// Reads an invoice photo with an AI model (OpenRouter in Infrastructure, called only from the background job, AGENTS.md
/// rule 14). A new provider is a new implementation (SOLID O). The extractor decides only whether an output is usable;
/// the job handler writes the <c>AiUsageRecord</c>s from <see cref="AiExtractionResult.Attempts"/>.
/// </summary>
public interface IAiInvoiceExtractor
{
    Task<AiExtractionResult> ExtractAsync(AiExtractionRequest request, CancellationToken cancellationToken);
}

/// <summary>The image bytes and their real content type. Never logged (rule 12).</summary>
public sealed record AiExtractionRequest(ReadOnlyMemory<byte> Image, string ContentType);

/// <summary>
/// Success: <see cref="Extraction"/> (validated, at least one item) and the <see cref="Model"/> that produced it.
/// Failure: <see cref="Failure"/>. <see cref="RawContent"/> is the model output used, or the last one seen; it is stored
/// in <c>invoices.raw_ai_json</c> and never logged. <see cref="Attempts"/> has one entry per HTTP attempt, in order.
/// </summary>
public sealed record AiExtractionResult(
    ExtractedInvoice? Extraction,
    InvoiceFailureReason? Failure,
    string? Model,
    string? RawContent,
    IReadOnlyList<AiAttempt> Attempts);

/// <summary>One HTTP attempt: the model requested, its outcome and status, tokens, cost (USD) and latency.</summary>
public sealed record AiAttempt(
    string Model, AiCallOutcome Outcome, int? HttpStatus, int PromptTokens, int CompletionTokens, decimal CostUsd, int LatencyMs);
