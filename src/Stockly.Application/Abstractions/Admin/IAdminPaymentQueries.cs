using Stockly.Contracts.Admin;
using Stockly.Contracts.Common;
using Stockly.Domain.Payments;
using Stockly.Domain.Users;

namespace Stockly.Application.Abstractions.Admin;

/// <summary>
/// Cross-tenant payment reads for the platform admin (AD21, AD22; ADR-0005 update 2026-10-18): projections, no tracking.
/// Implemented in <c>Infrastructure/Persistence/Admin</c>; used only by <c>Application/Features/Admin</c>.
/// </summary>
public interface IAdminPaymentQueries
{
    /// <summary>AD21: a page of payments of every shop (or of <paramref name="shopId"/>), optionally of one status, newest first.</summary>
    Task<PagedResponse<AdminPayment>> ListAsync(
        Guid? shopId, PaymentStatus? status, int page, int pageSize, PreferredLanguage language, CancellationToken cancellationToken);

    /// <summary>AD22: payments paid in <c>[fromUtc, toUtc)</c>, in total and per method (methods without payments omitted).</summary>
    Task<PaymentSummary> SummaryAsync(DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken cancellationToken);
}
