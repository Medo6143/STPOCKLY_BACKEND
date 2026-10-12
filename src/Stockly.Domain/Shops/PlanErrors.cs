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
}
