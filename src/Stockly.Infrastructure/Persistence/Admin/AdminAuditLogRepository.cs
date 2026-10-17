using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Stockly.Application.Abstractions.Admin;
using Stockly.Contracts.Admin;
using Stockly.Contracts.Common;
using Stockly.Domain.Platform;

namespace Stockly.Infrastructure.Persistence.Admin;

/// <summary>The admin audit log (global, append-only). AD20 indexes in docs/erd.md §3.6.</summary>
internal sealed class AdminAuditLogRepository(StocklyDbContext db) : IAdminAuditLog
{
    public void Add(AdminAuditLog entry) => db.Set<AdminAuditLog>().Add(entry);

    // targetType (+ targetId): ix_admin_audit_logs_target_type_target_id_created_at; adminUserId:
    // ix_admin_audit_logs_admin_user_id_created_at; none: ix_admin_audit_logs_created_at. The admin's email by users PK.
    public async Task<PagedResponse<AuditLogEntry>> ListAsync(
        string? targetType, Guid? targetId, Guid? adminUserId, int page, int pageSize, CancellationToken cancellationToken)
    {
        var logs = db.Set<AdminAuditLog>().AsNoTracking();
        if (targetType is not null)
        {
            logs = logs.Where(log => log.TargetType == targetType);
        }

        if (targetId is { } target)
        {
            logs = logs.Where(log => log.TargetId == target);
        }

        if (adminUserId is { } admin)
        {
            logs = logs.Where(log => log.AdminUserId == admin);
        }

        var totalCount = await logs.CountAsync(cancellationToken);
        var rows = await logs
            .OrderByDescending(log => log.CreatedAt)
            .ThenByDescending(log => log.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(log => new
            {
                log.Id,
                log.AdminUserId,
                AdminEmail = db.Users.Where(user => user.Id == log.AdminUserId).Select(user => user.Email).FirstOrDefault(),
                log.Action,
                log.TargetType,
                log.TargetId,
                log.Reason,
                log.Before,
                log.After,
                log.CreatedAt,
            })
            .ToListAsync(cancellationToken);

        var items = rows
            .Select(row => new AuditLogEntry(
                row.Id,
                row.AdminUserId,
                row.AdminEmail ?? string.Empty,
                row.Action,
                row.TargetType,
                row.TargetId,
                row.Reason,
                ToJson(row.Before),
                ToJson(row.After),
                row.CreatedAt.UtcDateTime))
            .ToList();
        return new PagedResponse<AuditLogEntry>(items, page, pageSize, totalCount);
    }

    private static JsonElement? ToJson(string? json)
    {
        if (json is null)
        {
            return null;
        }

        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}
