namespace Stockly.Contracts.Admin;

/// <summary>
/// AD14: AI usage of one Africa/Cairo day. <see cref="Calls"/> counts OpenRouter calls (retries included, cache hits not);
/// <see cref="Failures"/> counts calls that did not succeed. <see cref="CostUsd"/> has 8 decimals.
/// </summary>
public sealed record AiUsageDay(
    DateOnly Date, int Calls, int CacheHits, int Failures, int InputTokens, int OutputTokens, string CostUsd);

/// <summary>AD15: AI usage of one shop over the range.</summary>
public sealed record AiUsageByShop(Guid ShopId, string ShopName, int Calls, int Failures, string CostUsd);

/// <summary>AD16: AI usage of one model over the range; <see cref="AvgLatencyMs"/> averages its calls.</summary>
public sealed record AiUsageByModel(string Model, int Calls, int Failures, string CostUsd, int AvgLatencyMs);
