using Stockly.Application.Abstractions.Messaging;
using Stockly.Contracts.Admin;
using Stockly.Contracts.Common;

namespace Stockly.Application.Features.Admin.GetAiUsageByShop;

/// <summary>AD15: a page of shops by AI cost over the range (default the last 30 days), highest cost first.</summary>
public sealed record GetAiUsageByShopQuery(DateOnly? From, DateOnly? To, int Page, int PageSize) : IQuery<PagedResponse<AiUsageByShop>>;
