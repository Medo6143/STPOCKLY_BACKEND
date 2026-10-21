using MediatR;
using Stockly.Application.Abstractions.Admin;
using Stockly.Application.Abstractions.Payments;
using Stockly.Application.Abstractions.Persistence;
using Stockly.Application.Abstractions.Time;
using Stockly.Domain.Common;
using Stockly.Domain.Payments;

namespace Stockly.Application.Features.Admin.ReconcilePayments;

/// <summary>
/// Up to <see cref="AdminLimits.ReconcileBatchSize"/> expired pending payments, oldest expiry first. For each: ask the
/// provider for the order's latest transaction (outside any transaction); a provider error skips the payment until the
/// next run. Then lock the payment; if it is still pending, a successful or failed matching transaction is applied by
/// <see cref="PaymentSettlement"/>, and anything else (no transaction, still waiting, mismatch) expires it.
/// </summary>
public sealed class ReconcilePaymentsCommandHandler(
    IAdminPaymentRepository payments,
    IPaymentGateway gateway,
    PaymentSettlement settlement,
    IUnitOfWork unitOfWork,
    IClock clock) : IRequestHandler<ReconcilePaymentsCommand, Result<ReconcileSummary>>
{
    public async Task<Result<ReconcileSummary>> Handle(ReconcilePaymentsCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var due = await payments.ListExpiredPendingAsync(clock.UtcNow, AdminLimits.ReconcileBatchSize, cancellationToken);
        int paid = 0, expired = 0, failed = 0, skipped = 0;
        foreach (var candidate in due)
        {
            SettlementOutcome? outcome;
            try
            {
                outcome = await ReconcileAsync(candidate, cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // The batch is oldest first, so a payment that keeps throwing would stop every later run for every
                // shop. It is skipped instead, and the job logs the skipped count.
                outcome = null;
            }

            switch (outcome)
            {
                case null:
                    skipped++;
                    break;
                case SettlementOutcome.Paid:
                    paid++;
                    break;
                case SettlementOutcome.Failed:
                    failed++;
                    break;
                case SettlementOutcome.Expired:
                    expired++;
                    break;
                default:
                    break;
            }
        }

        return new ReconcileSummary(due.Count, paid, expired, failed, skipped);
    }

    /// <summary>The outcome for one payment, or <c>null</c> when it is skipped until the next run.</summary>
    private async Task<SettlementOutcome?> ReconcileAsync(ExpiredPayment candidate, CancellationToken cancellationToken)
    {
        if (candidate.Provider != gateway.Provider)
        {
            return null;
        }

        var latest = await gateway.FindLatestTransactionAsync(candidate.ProviderOrderId, cancellationToken);
        if (latest.IsFailure)
        {
            return null;
        }

        return await unitOfWork.ExecuteInTransactionAsync(
            token => SettleAsync(candidate.PaymentId, latest.Value, token),
            _ => true,
            cancellationToken);
    }

    private async Task<SettlementOutcome> SettleAsync(Guid paymentId, ProviderTransaction? latest, CancellationToken cancellationToken)
    {
        var payment = await payments.FindForUpdateAsync(paymentId, cancellationToken);
        if (payment is null || payment.Status != PaymentStatus.Pending)
        {
            return SettlementOutcome.Unchanged;
        }

        if (latest is { Outcome: ProviderTransactionOutcome.Succeeded or ProviderTransactionOutcome.Failed })
        {
            var outcome = await settlement.ApplyAsync(payment, latest, cancellationToken);
            if (outcome is SettlementOutcome.Paid or SettlementOutcome.Failed)
            {
                return outcome;
            }
        }

        payment.Expire();
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return SettlementOutcome.Expired;
    }
}
