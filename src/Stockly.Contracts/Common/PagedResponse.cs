namespace Stockly.Contracts.Common;

/// <summary>A page of a list (contract §1.3). <see cref="TotalCount"/> counts every matching item, not just this page.</summary>
public sealed record PagedResponse<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount);
