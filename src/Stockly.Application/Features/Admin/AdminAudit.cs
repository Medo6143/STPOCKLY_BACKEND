using System.Text.Json;
using System.Text.Json.Serialization;
using Stockly.Application.Abstractions.Admin;
using Stockly.Application.Abstractions.Time;
using Stockly.Domain.Platform;

namespace Stockly.Application.Features.Admin;

/// <summary>
/// Writes one <see cref="AdminAuditLog"/> per admin write (contract §11.1), in the write's own transaction (the caller saves
/// the unit of work). <c>before</c>/<c>after</c> are the changed fields as camelCase JSON objects, enums as names.
/// </summary>
public sealed class AdminAudit(IAdminAuditLog log, IClock clock)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    public void Record(Guid adminUserId, string action, string targetType, Guid targetId, string? reason, object? before, object? after) =>
        log.Add(AdminAuditLog.Create(
            adminUserId, action, targetType, targetId, reason, Serialize(before), Serialize(after), clock.UtcNow));

    private static string? Serialize(object? value) => value is null ? null : JsonSerializer.Serialize(value, value.GetType(), Json);
}

/// <summary>The <c>action</c> of each admin write (contract §11.1).</summary>
public static class AdminActions
{
    public const string ShopSuspend = "Shop.Suspend";
    public const string ShopActivate = "Shop.Activate";
    public const string ShopChangeSubscription = "Shop.ChangeSubscription";
    public const string ShopSetQuotaOverride = "Shop.SetQuotaOverride";
    public const string ShopResetQuota = "Shop.ResetQuota";
    public const string PlanCreate = "Plan.Create";
    public const string PlanUpdate = "Plan.Update";
    public const string InvoiceRerun = "Invoice.Rerun";
}

/// <summary>The <c>target_type</c> of each admin write.</summary>
public static class AdminTargets
{
    public const string Shop = "Shop";
    public const string Plan = "Plan";
    public const string Invoice = "Invoice";
}
