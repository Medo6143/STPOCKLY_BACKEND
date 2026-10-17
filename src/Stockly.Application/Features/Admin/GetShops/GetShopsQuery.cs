using Stockly.Application.Abstractions.Messaging;
using Stockly.Contracts.Admin;
using Stockly.Contracts.Common;
using Stockly.Contracts.Shops;

namespace Stockly.Application.Features.Admin.GetShops;

/// <summary>
/// AD4: a page of every shop, newest first, optionally of one status. <see cref="Search"/> matches the shop name
/// (Arabic-normalized, contains) or the owner's email (prefix).
/// </summary>
public sealed record GetShopsQuery(string? Search, ShopStatus? Status, int Page, int PageSize) : IQuery<PagedResponse<AdminShopListItem>>;
