using Stockly.Application.Abstractions.Messaging;
using Stockly.Contracts.Dashboard;

namespace Stockly.Application.Features.Dashboard.GetDashboardSummary;

/// <summary>D1: the shop's day at a glance, cached 2 minutes per catalog version and Cairo day.</summary>
public sealed record GetDashboardSummaryQuery : IQuery<DashboardSummary>;
