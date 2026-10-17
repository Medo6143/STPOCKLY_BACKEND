using Stockly.Domain.Common;

namespace Stockly.Domain.Shops;

public static class PlanErrors
{
    public static readonly Error CodeRequired =
        Error.Validation("Plan.CodeRequired", "A plan needs a code.");

    public static readonly Error NameRequired =
        Error.Validation("Plan.NameRequired", "A plan needs an Arabic and an English name.");

    public static readonly Error NegativeQuota =
        Error.Validation("Plan.NegativeQuota", "The daily scan quota must not be negative.");

    public static readonly Error NoMembers =
        Error.Validation("Plan.NoMembers", "A plan must allow at least one member.");

    /// <summary>No such plan (admin endpoints AD8, AD13).</summary>
    public static readonly Error NotFound =
        Error.NotFound("Plan.NotFound", "The plan was not found.");

    /// <summary>Another plan already uses this code (AD12).</summary>
    public static readonly Error CodeTaken =
        Error.Conflict("Plan.CodeTaken", "A plan with this code already exists.");

    /// <summary>An inactive plan cannot be assigned to a shop (AD8).</summary>
    public static readonly Error Inactive =
        Error.Validation("Plan.Inactive", "An inactive plan cannot be assigned.");
}
