using MediatR;
using Stockly.Application.Abstractions.Identity;
using Stockly.Application.Abstractions.Persistence;
using Stockly.Application.Abstractions.Stock;
using Stockly.Application.Features.Auth;
using Stockly.Application.Validation;
using Stockly.Contracts.Products;
using Stockly.Domain.Common;

namespace Stockly.Application.Features.Products.AdjustStock;

public sealed class AdjustStockCommandHandler(ICurrentUser currentUser, IStockService stock, IUnitOfWork unitOfWork)
    : IRequestHandler<AdjustStockCommand, Result<StockMovementDto>>
{
    public async Task<Result<StockMovementDto>> Handle(AdjustStockCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (currentUser.UserId is not { } userId)
        {
            return AuthErrors.Unauthenticated;
        }

        var change = DecimalInput.Quantity(request.QuantityChange, FieldPath.ToCamelCase(nameof(request.QuantityChange)));
        if (change.IsFailure)
        {
            return change.Error;
        }

        var movement = await stock.AdjustAsync(request.ProductId, request.UnitId, change.Value, request.Note, userId, cancellationToken);
        if (movement.IsFailure)
        {
            return movement.Error;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return movement.Value.ToDto();
    }
}
