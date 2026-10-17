using Stockly.Domain.Common;

namespace Stockly.Application.Features.Admin;

/// <summary>Error codes of the platform admin API (contract §9, §11) that belong to no Domain type.</summary>
public static class AdminErrors
{
    /// <summary>AD6, AD7, AD9, AD10, AD19: the write needs a non-blank reason, kept in the audit log.</summary>
    public static readonly Error ReasonRequired =
        Error.Validation("Admin.ReasonRequired", "Admin writes need a reason.");
}
