using System.Globalization;
using System.Text;
using Stockly.Contracts.Shops;

namespace Stockly.Admin.Services;

/// <summary>The admin endpoints of contract §11.3, relative to the API base address. One place for every path.</summary>
public static class ApiRoutes
{
    public const string Login = "api/admin/auth/login";
    public const string Refresh = "api/admin/auth/refresh";
    public const string Logout = "api/admin/auth/logout";
    public const string Shops = "api/admin/shops";
    public const string Plans = "api/admin/plans";
    public const string DailyUsage = "api/admin/ai-usage/daily";
    public const string UsageByShop = "api/admin/ai-usage/by-shop";
    public const string UsageByModel = "api/admin/ai-usage/by-model";
    public const string FailedInvoices = "api/admin/invoices/failed";
    public const string Invoices = "api/admin/invoices";
    public const string AuditLogs = "api/admin/audit-logs";
    public const string HangfireSession = "api/admin/hangfire/session";

    public static string Shop(Guid id) => $"{Shops}/{id}";

    public static string SuspendShop(Guid id) => $"{Shop(id)}/suspend";

    public static string ActivateShop(Guid id) => $"{Shop(id)}/activate";

    public static string Subscription(Guid id) => $"{Shop(id)}/subscription";

    public static string QuotaOverride(Guid id) => $"{Shop(id)}/quota-override";

    public static string QuotaReset(Guid id) => $"{Shop(id)}/quota/reset";

    public static string Plan(Guid id) => $"{Plans}/{id}";

    public static string Invoice(Guid id) => $"{Invoices}/{id}";

    public static string Rerun(Guid id) => $"{Invoice(id)}/rerun";
}

/// <summary>Builds a query string; skips empty values and escapes the rest. Dates are <c>yyyy-MM-dd</c> (contract §1.2).</summary>
public sealed class QueryString(string path)
{
    private const string DateFormat = "yyyy-MM-dd";

    private readonly StringBuilder _builder = new(path);
    private bool _hasParameters;

    public QueryString Add(string name, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return this;
        }

        _builder.Append(_hasParameters ? '&' : '?')
            .Append(Uri.EscapeDataString(name))
            .Append('=')
            .Append(Uri.EscapeDataString(value));
        _hasParameters = true;
        return this;
    }

    public QueryString Add(string name, int value) => Add(name, value.ToString(CultureInfo.InvariantCulture));

    public QueryString Add(string name, Guid? value) => Add(name, value?.ToString());

    public QueryString Add(string name, DateOnly value) => Add(name, value.ToString(DateFormat, CultureInfo.InvariantCulture));

    public QueryString Add(string name, ShopStatus? value) => Add(name, value?.ToString());

    public override string ToString() => _builder.ToString();
}
