using MediatR;
using Stockly.Domain.Common;

namespace Stockly.Application.Features.Admin.ReconcilePayments;

/// <summary>
/// The reconcile job (ADR-0015 §4): pending payments past their expiry are checked with the provider; a payment the
/// provider took becomes Paid (and buys its month), anything else becomes Expired. A system job: no admin user, no audit
/// entry. Not wrapped in the transaction behavior: each payment gets its own short transaction, and the provider is
/// called outside it.
/// </summary>
public sealed record ReconcilePaymentsCommand : IRequest<Result<ReconcileSummary>>;

/// <summary>Counts of one run. <see cref="Skipped"/> payments could not be checked (provider error) and wait for the next run.</summary>
public sealed record ReconcileSummary(int Checked, int Paid, int Expired, int Failed, int Skipped);
