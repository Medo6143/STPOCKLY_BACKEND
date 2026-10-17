using MediatR;
using Stockly.Application.Abstractions.Admin;
using Stockly.Contracts.Admin;
using Stockly.Contracts.Common;
using Stockly.Domain.Common;

namespace Stockly.Application.Features.Admin.GetAuditLogs;

public sealed class GetAuditLogsQueryHandler(IAdminAuditLog auditLog)
    : IRequestHandler<GetAuditLogsQuery, Result<PagedResponse<AuditLogEntry>>>
{
    public async Task<Result<PagedResponse<AuditLogEntry>>> Handle(GetAuditLogsQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var targetType = string.IsNullOrWhiteSpace(request.TargetType) ? null : request.TargetType.Trim();
        return await auditLog.ListAsync(targetType, request.TargetId, request.AdminUserId, request.Page, request.PageSize, cancellationToken);
    }
}
