using Stockly.Application.Abstractions.Messaging;
using Stockly.Contracts.Billing;
using Stockly.Contracts.Common;

namespace Stockly.Application.Features.Billing.GetPayments;

/// <summary>B5: a page of the shop's payments, newest first.</summary>
public sealed record GetPaymentsQuery(int Page, int PageSize) : IQuery<PagedResponse<PaymentDto>>;
