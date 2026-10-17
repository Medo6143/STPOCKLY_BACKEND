using MediatR;
using Stockly.Contracts.Sales;
using Stockly.Domain.Common;

namespace Stockly.Application.Features.Sales.CreateSale;

/// <summary>
/// S1: records a sale and takes its stock out. Idempotent by <see cref="ClientRequestId"/> (ADR-0006): a repeat returns
/// the existing sale. Not wrapped in the transaction behavior: the handler owns its transaction, so a concurrent repeat
/// that loses the insert is answered with the winner after the rollback, and the catalog version is bumped only after
/// the commit.
/// </summary>
public sealed record CreateSaleCommand(Guid ClientRequestId, IReadOnlyList<SaleItemRequest>? Items, string? Note)
    : IRequest<Result<CreateSaleResult>>;

/// <summary>The sale and whether this request created it (201) or repeated an earlier one (200).</summary>
public sealed record CreateSaleResult(SaleDto Sale, bool Created);
