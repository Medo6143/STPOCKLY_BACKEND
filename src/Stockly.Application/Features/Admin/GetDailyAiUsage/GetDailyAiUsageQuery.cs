using Stockly.Application.Abstractions.Messaging;
using Stockly.Contracts.Admin;

namespace Stockly.Application.Features.Admin.GetDailyAiUsage;

/// <summary>
/// AD14: AI usage per Africa/Cairo day from <see cref="From"/> to <see cref="To"/> (inclusive, default the last 30 days),
/// of every shop or of <see cref="ShopId"/>. One row per day, days without usage included, oldest first.
/// </summary>
public sealed record GetDailyAiUsageQuery(DateOnly? From, DateOnly? To, Guid? ShopId) : IQuery<IReadOnlyList<AiUsageDay>>;
