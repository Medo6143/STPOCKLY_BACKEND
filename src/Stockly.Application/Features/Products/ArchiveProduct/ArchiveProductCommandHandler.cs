using MediatR;
using Stockly.Application.Abstractions.Persistence;
using Stockly.Domain.Common;
using Stockly.Domain.Products;

namespace Stockly.Application.Features.Products.ArchiveProduct;

public sealed class ArchiveProductCommandHandler(IProductRepository products, IUnitOfWork unitOfWork)
    : IRequestHandler<ArchiveProductCommand, Result>
{
    public async Task<Result> Handle(ArchiveProductCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var product = await products.FindAsync(request.ProductId, cancellationToken);
        if (product is null)
        {
            return ProductErrors.NotFound;
        }

        product.Archive();
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
