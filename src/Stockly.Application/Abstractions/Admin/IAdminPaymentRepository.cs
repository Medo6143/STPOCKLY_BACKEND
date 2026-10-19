using Stockly.Domain.Payments;
using Stockly.Domain.Shops;

namespace Stockly.Application.Abstractions.Admin;

/// <summary>
/// Cross-tenant payment and subscription writes of the platform side: the provider callback, the reconcile job and the
/// daily downgrade (ADR-0015 §4, §5). None of them has a shop token, so they reach across tenants like the admin
/// (ADR-0005 update 2026-10-18, AGENTS.md rule 9). Loaded rows are tracked and saved with the unit of work. Every
/// <c>ForUpdate</c> method locks with <c>FOR UPDATE</c> and must run inside a transaction; the lock order is always the
/// payment first, then the shop's subscription (AGENTS.md rule 4). Used only by <c>Application/Features/Admin</c>.
/// </summary>
public interface IAdminPaymentRepository
{
    /// <summary>The payment of the provider's order, locked and tracked; <c>null</c> when no payment has that order.</summary>
    Task<Payment?> FindByProviderOrderForUpdateAsync(
        PaymentProvider provider, string providerOrderId, CancellationToken cancellationToken);

    /// <summary>The payment, locked and tracked; <c>null</c> when it does not exist.</summary>
    Task<Payment?> FindForUpdateAsync(Guid paymentId, CancellationToken cancellationToken);

    /// <summary>Up to <paramref name="limit"/> pending payments whose expiry is at or before <paramref name="now"/>, oldest expiry first.</summary>
    Task<IReadOnlyList<ExpiredPayment>> ListExpiredPendingAsync(DateTimeOffset now, int limit, CancellationToken cancellationToken);

    /// <summary>The shop's active subscription, locked and tracked; <c>null</c> when it has none.</summary>
    Task<ShopSubscription?> FindActiveSubscriptionForUpdateAsync(Guid shopId, CancellationToken cancellationToken);

    /// <summary>
    /// Up to <paramref name="limit"/> shops whose active subscription is not on <paramref name="defaultPlanId"/> and whose
    /// last day is before <paramref name="today"/>.
    /// </summary>
    Task<IReadOnlyList<Guid>> ListShopsWithLapsedSubscriptionAsync(
        DateOnly today, Guid defaultPlanId, int limit, CancellationToken cancellationToken);
}

/// <summary>A pending payment past its expiry, for the reconcile job.</summary>
public sealed record ExpiredPayment(Guid PaymentId, Guid ShopId, PaymentProvider Provider, string ProviderOrderId);
