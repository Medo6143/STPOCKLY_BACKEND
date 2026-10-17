using Stockly.Domain.Common;

namespace Stockly.Domain.Platform;

/// <summary>
/// One platform-admin write (ADR-0005, contract §11.1): who did what to which target, why, and the changed fields before
/// and after as JSON objects. Append-only and global (not a tenant entity). Written in the same transaction as the change.
/// </summary>
public sealed class AdminAuditLog : Entity
{
    /// <summary>Longest stored action or target type name.</summary>
    public const int NameMaxLength = 64;

    private AdminAuditLog(
        Guid adminUserId,
        string action,
        string targetType,
        Guid targetId,
        string? reason,
        string? before,
        string? after,
        DateTimeOffset createdAt)
    {
        AdminUserId = adminUserId;
        Action = action;
        TargetType = targetType;
        TargetId = targetId;
        Reason = reason;
        Before = before;
        After = after;
        CreatedAt = createdAt;
    }

    public Guid AdminUserId { get; }

    /// <summary>What was done, e.g. <c>Shop.Suspend</c>.</summary>
    public string Action { get; }

    /// <summary>The kind of target, e.g. <c>Shop</c>.</summary>
    public string TargetType { get; }

    public Guid TargetId { get; }

    public string? Reason { get; }

    /// <summary>JSON object of the changed fields before the write; <c>null</c> when nothing existed before.</summary>
    public string? Before { get; }

    /// <summary>JSON object of the changed fields after the write; <c>null</c> when the write has no field state.</summary>
    public string? After { get; }

    public DateTimeOffset CreatedAt { get; }

    /// <summary>A new entry. A blank reason is stored as <c>null</c>; the JSON documents are stored as given.</summary>
    public static AdminAuditLog Create(
        Guid adminUserId,
        string action,
        string targetType,
        Guid targetId,
        string? reason,
        string? before,
        string? after,
        DateTimeOffset now)
    {
        if (adminUserId == Guid.Empty)
        {
            throw new ArgumentException("An audit entry needs the admin who acted.", nameof(adminUserId));
        }

        if (targetId == Guid.Empty)
        {
            throw new ArgumentException("An audit entry needs its target.", nameof(targetId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(action);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetType);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(action.Length, NameMaxLength, nameof(action));
        ArgumentOutOfRangeException.ThrowIfGreaterThan(targetType.Length, NameMaxLength, nameof(targetType));

        return new AdminAuditLog(
            adminUserId,
            action,
            targetType,
            targetId,
            string.IsNullOrWhiteSpace(reason) ? null : reason.Trim(),
            before,
            after,
            now);
    }
}
