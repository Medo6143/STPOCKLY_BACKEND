using Stockly.Domain.Common;

namespace Stockly.Domain.Platform;

/// <summary>
/// One AI call, retry or cache hit (AGENTS.md rule 18). Append-only. Feeds the admin cost dashboard. Cost is USD credits
/// (<c>numeric(14,8)</c>), not EGP money.
/// </summary>
public sealed class AiUsageRecord : Entity, ITenantEntity
{
    /// <summary>Decimal places of <see cref="CostUsd"/>.</summary>
    public const int CostDecimals = 8;

    private AiUsageRecord(
        Guid shopId,
        Guid? invoiceId,
        string model,
        int promptTokens,
        int completionTokens,
        decimal costUsd,
        int latencyMs,
        AiCallOutcome outcome,
        bool cacheHit,
        DateTimeOffset createdAt)
    {
        ShopId = shopId;
        InvoiceId = invoiceId;
        Model = model;
        PromptTokens = promptTokens;
        CompletionTokens = completionTokens;
        CostUsd = costUsd;
        LatencyMs = latencyMs;
        Outcome = outcome;
        CacheHit = cacheHit;
        CreatedAt = createdAt;
    }

    public Guid ShopId { get; }

    public Guid? InvoiceId { get; }

    public string Model { get; }

    public int PromptTokens { get; }

    public int CompletionTokens { get; }

    public decimal CostUsd { get; }

    public int LatencyMs { get; }

    public AiCallOutcome Outcome { get; }

    public bool CacheHit { get; }

    public DateTimeOffset CreatedAt { get; }

    /// <summary>
    /// Negative counters become 0. The cost is rounded to <see cref="CostDecimals"/> places, midpoint away from zero: it is
    /// a provider-reported measurement that must be kept (rule 18), unlike user-entered money, which is rejected.
    /// </summary>
    public static AiUsageRecord Create(
        Guid shopId,
        Guid? invoiceId,
        string model,
        int promptTokens,
        int completionTokens,
        decimal costUsd,
        int latencyMs,
        AiCallOutcome outcome,
        bool cacheHit,
        DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(model);

        return new AiUsageRecord(
            shopId,
            invoiceId,
            model.Trim(),
            Math.Max(0, promptTokens),
            Math.Max(0, completionTokens),
            decimal.Round(Math.Max(0m, costUsd), CostDecimals, MidpointRounding.AwayFromZero),
            Math.Max(0, latencyMs),
            outcome,
            cacheHit,
            now);
    }
}
