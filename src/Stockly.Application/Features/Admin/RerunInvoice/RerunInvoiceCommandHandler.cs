using MediatR;
using Stockly.Application.Abstractions.Admin;
using Stockly.Application.Abstractions.Caching;
using Stockly.Application.Abstractions.Identity;
using Stockly.Application.Abstractions.Jobs;
using Stockly.Application.Abstractions.Persistence;
using Stockly.Application.Features.Auth;
using Stockly.Application.Features.Invoices;
using Stockly.Contracts.Invoices;
using Stockly.Domain.Common;
using Stockly.Domain.Invoices;

namespace Stockly.Application.Features.Admin.RerunInvoice;

/// <summary>
/// One transaction: lock the invoice (any shop), <c>Failed → Processing</c>, audit. After the commit: queue the re-run
/// job and write the status cache, so the shop's app sees <c>Processing</c> at once.
/// </summary>
public sealed class RerunInvoiceCommandHandler(
    ICurrentUser currentUser,
    IAdminInvoiceQueries invoices,
    AdminAudit audit,
    IBackgroundJobScheduler jobs,
    IInvoiceStatusCache statusCache,
    IUnitOfWork unitOfWork) : IRequestHandler<RerunInvoiceCommand, Result<InvoiceDetail>>
{
    public async Task<Result<InvoiceDetail>> Handle(RerunInvoiceCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (currentUser.UserId is not { } adminId)
        {
            return AuthErrors.Unauthenticated;
        }

        var reason = AdminReason.Require(request.Reason);
        if (reason.IsFailure)
        {
            return reason.Error;
        }

        var outcome = await unitOfWork.ExecuteInTransactionAsync(
            async token =>
            {
                var invoice = await invoices.FindForUpdateAsync(request.InvoiceId, token);
                if (invoice is null)
                {
                    return new Outcome(InvoiceErrors.NotFound, null);
                }

                var before = new { Status = invoice.Status, FailureReason = invoice.FailureReason };
                var rerun = invoice.Rerun();
                if (rerun.IsFailure)
                {
                    return new Outcome(rerun.Error, null);
                }

                audit.Record(
                    adminId, AdminActions.InvoiceRerun, AdminTargets.Invoice, invoice.Id, reason.Value, before, new { Status = invoice.Status });
                await unitOfWork.SaveChangesAsync(token);
                return new Outcome(Result.Success(), invoice);
            },
            outcome => outcome.Invoice is not null,
            cancellationToken);

        if (outcome.Invoice is not { } queued)
        {
            return outcome.Result.Error;
        }

        // Cached before the job is queued: a fast job's status must not be overwritten by this Processing.
        await statusCache.SetAsync(queued.ShopId, queued.ToStatusSnapshot(), AfterCommit.Token);
        jobs.EnqueueRerunInvoice(queued.ShopId, queued.Id);

        var detail = await invoices.FindAsync(queued.Id, cancellationToken)
            ?? throw new InvalidOperationException("The invoice queued again was not found.");
        return detail.Invoice;
    }

    private sealed record Outcome(Result Result, Invoice? Invoice);
}
