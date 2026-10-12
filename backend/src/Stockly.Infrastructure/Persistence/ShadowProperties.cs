namespace Stockly.Infrastructure.Persistence;

/// <summary>EF properties that exist in the database but not on Domain types.</summary>
public static class ShadowProperties
{
    /// <summary>Set on insert and update by the <c>AuditTimestampsInterceptor</c>.</summary>
    public const string UpdatedAt = "UpdatedAt";

    /// <summary>PostgreSQL system column used as the optimistic concurrency token.</summary>
    public const string RowVersion = "xmin";
}
