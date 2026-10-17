using Stockly.Application.Abstractions.Messaging;
using Stockly.Contracts.Admin;
using Stockly.Contracts.Common;

namespace Stockly.Application.Features.Admin.GetAuditLogs;

/// <summary>AD20: a page of admin audit entries, newest first, optionally of one target type, target or admin.</summary>
public sealed record GetAuditLogsQuery(string? TargetType, Guid? TargetId, Guid? AdminUserId, int Page, int PageSize)
    : IQuery<PagedResponse<AuditLogEntry>>;
