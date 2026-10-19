using Stockly.Application.Abstractions.Admin;
using Stockly.Application.Abstractions.Payments;
using Stockly.Application.Abstractions.Persistence;
using Stockly.Application.Abstractions.Time;
using Stockly.Domain.Payments;
using Stockly.Domain.Shops;

namespace Stockly.Application.Features.Admin;

/// <summary>
/// Applies a verified provider transaction to a payment (ADR-0015 §4, §5), for the callback and the reconcile job. The
/// caller has locked the payment inside its transaction; this class then locks the shop's active subscription (payment
/// first, then subscription: AGENTS.md rule 4) and saves everything in that transaction. A transaction whose order,
/// amount or currency differs from the payment never makes it Paid. Paid buys one month: the same plan is extended, any
/// other plan is ended today and replaced by the paid plan from today; the quota override is kept.
/// </summary>
public sealed class PaymentSettlement(
    IAdminPaymentRepository payments,
    IAdminShopRepository shops,
    IUnitOfWork unitOfWork,
    IClock clock)
{
    public async Task<SettlementOutcome> ApplyAsync(Payment payment, ProviderTransaction transaction, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(payment);
        ArgumentNullException.ThrowIfNull(transaction);

        if (!payment.Matches(transaction.ProviderOrderId, transaction.Amount, transaction.Currency))
        {
            return SettlementOutcome.Mismatch;
        }

        switch (transaction.Outcome)
        {
            case ProviderTransactionOutcome.Succeeded:
                return await PayAsync(payment, transaction.TransactionId, cancellationToken);
            case ProviderTransactionOutcome.Failed:
                var failed = payment.MarkFailed(transaction.TransactionId, transaction.FailureReason);
                if (failed.IsFailure || !failed.Value)
                {
                    return SettlementOutcome.Unchanged;
                }

                await unitOfWork.SaveChangesAsync(cancellationToken);
                return SettlementOutcome.Failed;
            default:
                return SettlementOutcome.StillPending;
        }
    }

    private async Task<SettlementOutcome> PayAsync(Payment payment, string transactionId, CancellationToken cancellationToken)
    {
        var paid = payment.MarkPaid(transactionId, clock.UtcNow);
        if (paid.IsFailure || !paid.Value)
        {
            return SettlementOutcome.Unchanged;
        }

        var today = clock.Today();
        var current = await payments.FindActiveSubscriptionForUpdateAsync(payment.ShopId, cancellationToken);
        if (current is not null && current.PlanId == payment.PlanId)
        {
            var extended = current.ExtendOneMonth(today);
            if (extended.IsFailure)
            {
                throw new InvalidOperationException($"The active subscription could not be extended: {extended.Error.Code}.");
            }

            await unitOfWork.SaveChangesAsync(cancellationToken);
            return SettlementOutcome.Paid;
        }

        current?.End(today);
        var next = ShopSubscription.StartPaidMonth(payment.ShopId, payment.PlanId, today, current?.DailyScanQuotaOverride, clock.UtcNow);
        if (next.IsFailure)
        {
            throw new InvalidOperationException($"The paid subscription could not be started: {next.Error.Code}.");
        }

        // Saves the payment and the ended subscription first, then inserts the new one (one active per shop).
        await shops.AddSubscriptionAsync(next.Value, cancellationToken);
        return SettlementOutcome.Paid;
    }
}

/// <summary>What a provider transaction did to a payment. Logged by payment id only.</summary>
public enum SettlementOutcome
{
    /// <summary>The payment became Paid and the subscription was extended or switched.</summary>
    Paid = 1,

    /// <summary>The payment became Failed.</summary>
    Failed = 2,

    /// <summary>A repeat or a late notice: nothing changed (Paid is final, a failure only fails a pending payment).</summary>
    Unchanged = 3,

    /// <summary>The provider is still waiting (e.g. an unpaid kiosk reference).</summary>
    StillPending = 4,

    /// <summary>Order, amount or currency differ from the stored payment: never Paid.</summary>
    Mismatch = 5,

    /// <summary>No payment has the provider's order (callbacks only).</summary>
    UnknownOrder = 6,

    /// <summary>The reconcile job found nothing paid at the provider and expired the payment.</summary>
    Expired = 7,
}
