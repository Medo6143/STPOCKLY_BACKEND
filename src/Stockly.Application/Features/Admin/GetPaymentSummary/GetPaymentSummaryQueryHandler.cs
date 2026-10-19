using MediatR;
using Stockly.Application.Abstractions.Admin;
using Stockly.Application.Abstractions.Time;
using Stockly.Contracts.Admin;
using Stockly.Domain.Common;

namespace Stockly.Application.Features.Admin.GetPaymentSummary;

/// <summary>The range resolves like AD14 (<see cref="UsageRange"/>); a payment counts on the Cairo day it was paid.</summary>
public sealed class GetPaymentSummaryQueryHandler(IAdminPaymentQueries payments, IClock clock)
    : IRequestHandler<GetPaymentSummaryQuery, Result<PaymentSummary>>
{
    public async Task<Result<PaymentSummary>> Handle(GetPaymentSummaryQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var range = UsageRange.Resolve(request.From, request.To, clock);
        return await payments.SummaryAsync(range.StartUtc, range.EndUtc, cancellationToken);
    }
}
