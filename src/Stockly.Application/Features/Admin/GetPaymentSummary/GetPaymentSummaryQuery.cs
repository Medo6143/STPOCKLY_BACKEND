using Stockly.Application.Abstractions.Messaging;
using Stockly.Contracts.Admin;

namespace Stockly.Application.Features.Admin.GetPaymentSummary;

/// <summary>
/// AD22: payments paid from <see cref="From"/> to <see cref="To"/> (Africa/Cairo days, both included, default the last
/// 30 days, at most 92 days apart), in total and per method.
/// </summary>
public sealed record GetPaymentSummaryQuery(DateOnly? From, DateOnly? To) : IQuery<PaymentSummary>;
