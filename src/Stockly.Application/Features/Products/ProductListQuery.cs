using FluentValidation;
using Stockly.Application.Abstractions.Persistence;
using Stockly.Application.Validation;
using Stockly.Domain.Text;

namespace Stockly.Application.Features.Products;

/// <summary>The P1 query parameters shared by <c>GetProductsQuery</c> and <c>GetLowStockQuery</c>.</summary>
public interface IProductListQuery
{
    string? Search { get; }

    bool IncludeArchived { get; }

    int Page { get; }

    int PageSize { get; }
}

/// <summary>P1 request shape: search length and paging (<c>pageSize &gt; 100</c> gives <c>400 Validation.Failed</c>).</summary>
public abstract class ProductListQueryValidator<TQuery> : AbstractValidator<TQuery>
    where TQuery : IProductListQuery
{
    protected ProductListQueryValidator()
    {
        RuleFor(q => q.Search).MaximumLength(ProductLimits.SearchMaxLength);
        RuleFor(q => q.Page).GreaterThanOrEqualTo(Paging.DefaultPage);
        RuleFor(q => q.PageSize).InclusiveBetween(1, Paging.MaxPageSize);
    }
}

public static class ProductListQueryExtensions
{
    /// <summary>The filter for <see cref="IProductQueries.ListAsync"/>: the search Arabic-normalized, blank means no search.</summary>
    public static ProductListFilter ToFilter(this IProductListQuery query, bool lowStockOnly)
    {
        ArgumentNullException.ThrowIfNull(query);

        var search = query.Search is null ? string.Empty : ArabicTextNormalizer.Normalize(query.Search);
        return new ProductListFilter(search.Length == 0 ? null : search, lowStockOnly, query.IncludeArchived, query.Page, query.PageSize);
    }
}
