using Stockly.Contracts.Invoices;

namespace Stockly.Contracts.Admin;

/// <summary>
/// AD17: a failed scan. <see cref="Attempts"/> counts its OpenRouter calls (retries included); <see cref="UpdatedAt"/> is
/// when it was last processed.
/// </summary>
public sealed record AdminFailedInvoice(
    Guid InvoiceId, Guid ShopId, string ShopName, string? FailureReason, int Attempts, DateTime CreatedAt, DateTime UpdatedAt);

/// <summary>
/// AD18: an invoice of any shop with what the admin needs to debug it. <see cref="ImageUrl"/> is a pre-signed GET valid
/// 5 minutes (<c>null</c> when the object is gone). <see cref="RawAiJson"/> is shown to the admin only and never logged.
/// </summary>
public sealed record AdminInvoiceDetail(
    InvoiceDetail Invoice,
    Guid ShopId,
    string ShopName,
    string? ImageUrl,
    string? RawAiJson,
    IReadOnlyList<AdminAiUsageDto> Usage);

/// <summary>One AI usage row of the invoice, oldest first. <see cref="CostUsd"/> has 8 decimals.</summary>
public sealed record AdminAiUsageDto(
    Guid Id,
    string Model,
    bool IsCacheHit,
    bool Succeeded,
    int InputTokens,
    int OutputTokens,
    string CostUsd,
    int LatencyMs,
    DateTime CreatedAt);
