using Stockly.Application.Abstractions.Messaging;
using Stockly.Contracts.Admin;
using Stockly.Contracts.Billing;
using Stockly.Contracts.Common;

namespace Stockly.Application.Features.Admin.GetPayments;

/// <summary>AD21: a page of payments of every shop (or of <see cref="ShopId"/>), optionally of one status, newest first.</summary>
public sealed record GetPaymentsQuery(Guid? ShopId, PaymentStatus? Status, int Page, int PageSize) : IQuery<PagedResponse<AdminPayment>>;
