using MediatR;
using Stockly.Contracts.Admin;
using Stockly.Domain.Common;

namespace Stockly.Application.Features.Admin.CreateHangfireSession;

/// <summary>
/// <c>POST /api/admin/hangfire/session</c> (contract §11.3): a one-time URL that opens the Hangfire dashboard, valid
/// 60 seconds and single use. Writes no database row, so it is not an <c>ICommand</c> and not audited.
/// </summary>
public sealed record CreateHangfireSessionCommand : IRequest<Result<HangfireSessionResponse>>;
