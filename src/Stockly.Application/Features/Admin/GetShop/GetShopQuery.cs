using Stockly.Application.Abstractions.Messaging;
using Stockly.Contracts.Admin;

namespace Stockly.Application.Features.Admin.GetShop;

/// <summary>AD5: one shop of any tenant with its members, subscription, counts, today's scans and the AI cost of 30 days.</summary>
public sealed record GetShopQuery(Guid ShopId) : IQuery<AdminShopDetail>;
