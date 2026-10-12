using MediatR;
using Stockly.Application.Abstractions.Identity;
using Stockly.Application.Abstractions.Persistence;
using Stockly.Application.Abstractions.Stock;
using Stockly.Application.Features.Auth;
using Stockly.Application.Validation;
using Stockly.Contracts.Products;
using Stockly.Domain.Common;

namespace Stockly.Application.Features.Products.CountStock;

public sealed class CountStockCommandHandler(ICurrentUser currentUser, IStockService stock, IUnitOfWork unitOfWork)
    : IRequestHandler<CountStockCommand, Result<StockCountResponse>>
{
    public async Task<Result<StockCountResponse>> Handle(CountStockCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (currentUser.UserId is not { } userId)
        {
            return AuthErrors.Unauthenticated;
        }

        var counted = DecimalInput.NonNegativeQuantity(request.CountedQuantity, FieldPath.ToCamelCase(nameof(request.CountedQuantity)));
        if (counted.IsFailure)
        {
            return counted.Error;
        }

        var count = await stock.CountAsync(request.ProductId, request.UnitId, counted.Value, userId, cancellationToken);
        if (count.IsFailure)
        {
            return count.Error;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        var movement = count.Value.Outcome.Movement;
        return new StockCountResponse(movement is not null, ProductMapping.Quantity(count.Value.Quantity), movement?.ToDto());
    }
}
