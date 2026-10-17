using MediatR;
using Stockly.Application.Abstractions.Caching;
using Stockly.Application.Abstractions.Identity;
using Stockly.Application.Abstractions.Persistence;
using Stockly.Application.Abstractions.Time;
using Stockly.Domain.Common;
using Stockly.Domain.Invoices;

namespace Stockly.Application.Features.Invoices.FailInvoiceProcessing;

public sealed class FailInvoiceProcessingCommandHandler(
    ICurrentTenant currentTenant,
    IInvoiceRepository invoices,
    IInvoiceStatusCache statusCache,
    IUnitOfWork unitOfWork,
    IClock clock) : IRequestHandler<FailInvoiceProcessingCommand, Result>
{
    public async Task<Result> Handle(FailInvoiceProcessingCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (currentTenant.ShopId is not { } shopId)
        {
            return InvoiceErrors.NotFound;
        }

        // Lock the invoice and re-check its status, so a run that finished meanwhile is never overwritten.
        var failed = await unitOfWork.ExecuteInTransactionAsync(
            async token =>
            {
                var invoice = await invoices.FindForUpdateAsync(request.InvoiceId, token);
                if (invoice is null)
                {
                    return Result.Failure<Invoice>(InvoiceErrors.NotFound);
                }

                var marked = invoice.MarkFailed(InvoiceFailureReason.Unexpected, rawAiJson: null, clock.UtcNow);
                if (marked.IsFailure)
                {
                    return Result.Failure<Invoice>(marked.Error);
                }

                await unitOfWork.SaveChangesAsync(token);
                return Result.Success(invoice);
            },
            result => result.IsSuccess,
            cancellationToken);

        if (failed.IsFailure)
        {
            return failed.Error;
        }

        await statusCache.SetAsync(shopId, failed.Value.ToStatusSnapshot(), cancellationToken);
        return Result.Success();
    }
}
