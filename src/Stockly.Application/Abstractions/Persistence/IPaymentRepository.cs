using Stockly.Domain.Payments;

namespace Stockly.Application.Abstractions.Persistence;

/// <summary>Payment writes and decisions for the current shop (tenant-filtered and tenant-stamped, ADR-0002).</summary>
public interface IPaymentRepository
{
    void Add(Payment payment);

    /// <summary>
    /// Locks the payment row (<c>FOR UPDATE</c>) and loads it, tracked; <c>null</c> when missing or another shop's. Must
    /// run inside a transaction: the payment is always the first row locked (ADR-0015 §5, AGENTS.md rule 4).
    /// </summary>
    Task<Payment?> FindForUpdateAsync(Guid paymentId, CancellationToken cancellationToken);

    /// <summary>
    /// Serializes checkouts of the current shop: locks its active subscription row until the transaction ends, so two
    /// concurrent checkouts cannot both pass the pending-payment check. Must run inside a transaction.
    /// </summary>
    Task LockCheckoutsAsync(CancellationToken cancellationToken);

    /// <summary>Whether the shop has a pending payment whose expiry is after <paramref name="now"/> (B3 <c>Billing.PaymentPending</c>).</summary>
    Task<bool> HasOpenPaymentAsync(DateTimeOffset now, CancellationToken cancellationToken);
}
