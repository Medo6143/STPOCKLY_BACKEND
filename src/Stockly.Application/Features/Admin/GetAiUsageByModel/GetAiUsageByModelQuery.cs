using Stockly.Application.Abstractions.Messaging;
using Stockly.Contracts.Admin;

namespace Stockly.Application.Features.Admin.GetAiUsageByModel;

/// <summary>AD16: every model by AI cost over the range (default the last 30 days), highest cost first.</summary>
public sealed record GetAiUsageByModelQuery(DateOnly? From, DateOnly? To) : IQuery<IReadOnlyList<AiUsageByModel>>;
