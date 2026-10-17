using Stockly.Contracts.Admin;
using Stockly.Contracts.Common;
using Stockly.Domain.Platform;

namespace Stockly.Application.Abstractions.Admin;

/// <summary>The admin audit log (ADR-0005, contract §11.1). Used only by <c>Application/Features/Admin</c>.</summary>
public interface IAdminAuditLog
{
    /// <summary>Adds an entry, saved with the unit of work in the write's own transaction.</summary>
    void Add(AdminAuditLog entry);

    /// <summary>AD20: a page of entries, newest first, filtered by any of the given values.</summary>
    Task<PagedResponse<AuditLogEntry>> ListAsync(
        string? targetType, Guid? targetId, Guid? adminUserId, int page, int pageSize, CancellationToken cancellationToken);
}
