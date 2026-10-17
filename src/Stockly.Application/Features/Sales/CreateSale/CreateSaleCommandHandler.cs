using MediatR;
using Stockly.Application.Abstractions.Caching;
using Stockly.Application.Abstractions.Identity;
using Stockly.Application.Abstractions.Persistence;
using Stockly.Application.Abstractions.Stock;
using Stockly.Application.Abstractions.Time;
using Stockly.Application.Features.Auth;
using Stockly.Application.Validation;
using Stockly.Contracts.Sales;
using Stockly.Domain.Common;
using Stockly.Domain.Products;
using Stockly.Domain.Sales;

namespace Stockly.Application.Features.Sales.CreateSale;

/// <summary>
/// S1 (contract §7), the confirm transaction's pattern (rules 1, 3, 4, 5, 20, 23, 24): parse the decimal strings; return an
/// existing sale with this client request id; else, in one transaction, insert the sale header first (a concurrent
/// repeat waits on <c>ux_sales_shop_id_client_request_id</c> and then loses), lock the products in one ordered
/// statement, add each line and take its stock out through <see cref="IStockService"/> in base units, save, and return
/// the projection. Negative stock never blocks; it is reported in the sale's warnings. After the commit the catalog
/// version is bumped (rule 23).
/// </summary>
public sealed class CreateSaleCommandHandler(
    ICurrentUser currentUser,
    ICurrentTenant currentTenant,
    ISaleRepository sales,
    ISaleQueries queries,
    IProductRepository products,
    IStockService stock,
    IUnitOfWork unitOfWork,
    ICacheService cache,
    IClock clock) : IRequestHandler<CreateSaleCommand, Result<CreateSaleResult>>
{
    public async Task<Result<CreateSaleResult>> Handle(CreateSaleCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Items);

        if (currentUser.UserId is not { } userId || currentTenant.ShopId is not { } shopId)
        {
            return AuthErrors.Unauthenticated;
        }

        var lines = Parse(request.Items);
        if (lines.IsFailure)
        {
            return lines.Error;
        }

        var existing = await queries.FindByClientRequestIdAsync(request.ClientRequestId, cancellationToken);
        if (existing is not null)
        {
            return new CreateSaleResult(existing, Created: false);
        }

        var recorded = await unitOfWork.ExecuteInTransactionAsync(
            token => RecordAsync(request, lines.Value, shopId, userId, token),
            result => result.IsSuccess,
            cancellationToken);

        if (recorded.IsFailure)
        {
            if (recorded.Error != SaleErrors.DuplicateClientRequest)
            {
                return recorded.Error;
            }

            // A concurrent first request with the same client request id committed first: return its sale.
            var winner = await queries.FindByClientRequestIdAsync(request.ClientRequestId, cancellationToken)
                ?? throw new InvalidOperationException("The sale that won the client request id was not found.");
            return new CreateSaleResult(winner, Created: false);
        }

        await cache.IncrementVersionAsync(CacheKeys.CatalogVersion(shopId), cancellationToken);
        return new CreateSaleResult(recorded.Value, Created: true);
    }

    private async Task<Result<SaleDto>> RecordAsync(
        CreateSaleCommand request, List<SaleLine> lines, Guid shopId, Guid userId, CancellationToken cancellationToken)
    {
        var sale = Sale.Start(shopId, request.ClientRequestId, userId, request.Note, clock.UtcNow);
        if (sale.IsFailure)
        {
            return sale.Error;
        }

        // The sale first (rule 4): the header insert takes the client request id's unique key until the commit.
        sales.Add(sale.Value);
        var header = await unitOfWork.TrySaveChangesAsync(cancellationToken);
        if (header.IsFailure)
        {
            return header.Error;
        }

        // Then the products, in one ordered statement; quantities come from these rows (rule 24).
        var locked = await stock.LockProductsAsync([.. lines.Select(line => line.ProductId).Distinct()], cancellationToken);
        foreach (var line in lines)
        {
            var error = await SellAsync(sale.Value, line, locked, userId, cancellationToken);
            if (error is not null)
            {
                return error;
            }
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        var detail = await queries.FindAsync(sale.Value.Id, cancellationToken);
        return detail ?? throw new InvalidOperationException("The sale saved in this transaction was not found.");
    }

    // Lines in request order: the first failing line decides the error. Another shop's product is invisible (404).
    private async Task<Error?> SellAsync(
        Sale sale, SaleLine line, IReadOnlyDictionary<Guid, Product> locked, Guid userId, CancellationToken cancellationToken)
    {
        if (!locked.TryGetValue(line.ProductId, out var product))
        {
            return ProductErrors.NotFound;
        }

        var unit = product.Units.FirstOrDefault(candidate => candidate.Id == line.ProductUnitId);
        if (unit is null)
        {
            return await products.UnitExistsAsync(line.ProductUnitId, cancellationToken)
                ? SaleErrors.UnitNotOfProduct
                : ProductUnitErrors.NotFound;
        }

        var item = sale.AddItem(product, unit, line.Quantity, line.UnitPrice);
        if (item.IsFailure)
        {
            return item.Error;
        }

        var movement = stock.RecordSale(product, unit.ToBase(line.Quantity), sale.Id, item.Value.Id, userId);
        return movement.IsFailure ? movement.Error : null;
    }

    private static Result<List<SaleLine>> Parse(IReadOnlyList<SaleItemRequest> items)
    {
        var lines = new List<SaleLine>(items.Count);
        for (var index = 0; index < items.Count; index++)
        {
            var item = items[index];
            var field = FieldPath.ToCamelCase($"{nameof(CreateSaleCommand.Items)}[{index}]") + ".";
            var quantity = DecimalInput.Quantity(item.Quantity, field + FieldPath.ToCamelCase(nameof(item.Quantity)));
            var unitPrice = item.UnitPrice is null
                ? Result.Success<decimal?>(null)
                : ToNullable(DecimalInput.Money(item.UnitPrice, field + FieldPath.ToCamelCase(nameof(item.UnitPrice))));
            if (DecimalInput.FirstError(quantity, unitPrice) is { } error)
            {
                return error;
            }

            lines.Add(new SaleLine(item.ProductId, item.ProductUnitId, quantity.Value, unitPrice.Value));
        }

        return lines;
    }

    private static Result<decimal?> ToNullable(Result<decimal> parsed) =>
        parsed.IsSuccess ? Result.Success<decimal?>(parsed.Value) : Result.Failure<decimal?>(parsed.Error);

    private sealed record SaleLine(Guid ProductId, Guid ProductUnitId, decimal Quantity, decimal? UnitPrice);
}
