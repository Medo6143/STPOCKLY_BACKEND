using Stockly.Contracts.Admin;
using Stockly.Contracts.Auth;
using Stockly.Contracts.Common;
using Stockly.Contracts.Invoices;
using Stockly.Contracts.Shops;

namespace Stockly.Admin.Services;

/// <summary>
/// Typed client of the authenticated admin endpoints (contract §11, AD3–AD20 and the Hangfire session). Its
/// <see cref="HttpClient"/> comes from <c>IHttpClientFactory</c> with <see cref="Auth.BearerTokenHandler"/>, which adds the
/// token and refreshes it once on <c>401</c>. Every method returns the value or the problem to show; none throws for an
/// API error.
/// </summary>
public sealed class AdminApiClient(HttpClient http)
{
    public Task<ApiResult<PagedResponse<AdminShopListItem>>> GetShopsAsync(
        string? search, ShopStatus? status, int page, int pageSize, CancellationToken cancellationToken)
    {
        var path = new QueryString(ApiRoutes.Shops)
            .Add(QueryNames.Search, search?.Trim())
            .Add(QueryNames.Status, status)
            .Add(QueryNames.Page, page)
            .Add(QueryNames.PageSize, pageSize);
        return HttpCalls.GetAsync<PagedResponse<AdminShopListItem>>(http, path.ToString(), cancellationToken);
    }

    public Task<ApiResult<AdminShopDetail>> GetShopAsync(Guid id, CancellationToken cancellationToken) =>
        HttpCalls.GetAsync<AdminShopDetail>(http, ApiRoutes.Shop(id), cancellationToken);

    public Task<ApiResult<NoContent>> SuspendShopAsync(Guid id, string reason, CancellationToken cancellationToken) =>
        HttpCalls.PostAsync<NoContent>(http, ApiRoutes.SuspendShop(id), new AdminReasonRequest(reason), cancellationToken);

    public Task<ApiResult<NoContent>> ActivateShopAsync(Guid id, string reason, CancellationToken cancellationToken) =>
        HttpCalls.PostAsync<NoContent>(http, ApiRoutes.ActivateShop(id), new AdminReasonRequest(reason), cancellationToken);

    public Task<ApiResult<AdminSubscriptionDto>> ChangeSubscriptionAsync(
        Guid id, ChangeSubscriptionRequest request, CancellationToken cancellationToken) =>
        HttpCalls.PutAsync<AdminSubscriptionDto>(http, ApiRoutes.Subscription(id), request, cancellationToken);

    public Task<ApiResult<AdminSubscriptionDto>> SetQuotaOverrideAsync(
        Guid id, QuotaOverrideRequest request, CancellationToken cancellationToken) =>
        HttpCalls.PutAsync<AdminSubscriptionDto>(http, ApiRoutes.QuotaOverride(id), request, cancellationToken);

    public Task<ApiResult<NoContent>> ResetQuotaAsync(Guid id, string reason, CancellationToken cancellationToken) =>
        HttpCalls.PostAsync<NoContent>(http, ApiRoutes.QuotaReset(id), new AdminReasonRequest(reason), cancellationToken);

    public Task<ApiResult<IReadOnlyList<PlanDto>>> GetPlansAsync(CancellationToken cancellationToken) =>
        HttpCalls.GetAsync<IReadOnlyList<PlanDto>>(http, ApiRoutes.Plans, cancellationToken);

    public Task<ApiResult<PlanDto>> CreatePlanAsync(CreatePlanRequest request, CancellationToken cancellationToken) =>
        HttpCalls.PostAsync<PlanDto>(http, ApiRoutes.Plans, request, cancellationToken);

    public Task<ApiResult<PlanDto>> UpdatePlanAsync(Guid id, UpdatePlanRequest request, CancellationToken cancellationToken) =>
        HttpCalls.PutAsync<PlanDto>(http, ApiRoutes.Plan(id), request, cancellationToken);

    public Task<ApiResult<IReadOnlyList<AiUsageDay>>> GetDailyUsageAsync(
        DateOnly from, DateOnly to, Guid? shopId, CancellationToken cancellationToken)
    {
        var path = new QueryString(ApiRoutes.DailyUsage)
            .Add(QueryNames.From, from)
            .Add(QueryNames.To, to)
            .Add(QueryNames.ShopId, shopId);
        return HttpCalls.GetAsync<IReadOnlyList<AiUsageDay>>(http, path.ToString(), cancellationToken);
    }

    public Task<ApiResult<PagedResponse<AiUsageByShop>>> GetUsageByShopAsync(
        DateOnly from, DateOnly to, int page, int pageSize, CancellationToken cancellationToken)
    {
        var path = new QueryString(ApiRoutes.UsageByShop)
            .Add(QueryNames.From, from)
            .Add(QueryNames.To, to)
            .Add(QueryNames.Page, page)
            .Add(QueryNames.PageSize, pageSize);
        return HttpCalls.GetAsync<PagedResponse<AiUsageByShop>>(http, path.ToString(), cancellationToken);
    }

    public Task<ApiResult<IReadOnlyList<AiUsageByModel>>> GetUsageByModelAsync(
        DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        var path = new QueryString(ApiRoutes.UsageByModel)
            .Add(QueryNames.From, from)
            .Add(QueryNames.To, to);
        return HttpCalls.GetAsync<IReadOnlyList<AiUsageByModel>>(http, path.ToString(), cancellationToken);
    }

    public Task<ApiResult<PagedResponse<AdminFailedInvoice>>> GetFailedInvoicesAsync(
        Guid? shopId, int page, int pageSize, CancellationToken cancellationToken)
    {
        var path = new QueryString(ApiRoutes.FailedInvoices)
            .Add(QueryNames.ShopId, shopId)
            .Add(QueryNames.Page, page)
            .Add(QueryNames.PageSize, pageSize);
        return HttpCalls.GetAsync<PagedResponse<AdminFailedInvoice>>(http, path.ToString(), cancellationToken);
    }

    public Task<ApiResult<AdminInvoiceDetail>> GetInvoiceAsync(Guid id, CancellationToken cancellationToken) =>
        HttpCalls.GetAsync<AdminInvoiceDetail>(http, ApiRoutes.Invoice(id), cancellationToken);

    public Task<ApiResult<InvoiceDetail>> RerunInvoiceAsync(Guid id, string reason, CancellationToken cancellationToken) =>
        HttpCalls.PostAsync<InvoiceDetail>(http, ApiRoutes.Rerun(id), new AdminReasonRequest(reason), cancellationToken);

    public Task<ApiResult<PagedResponse<AuditLogEntry>>> GetAuditLogAsync(
        AuditLogFilter filter, int page, int pageSize, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filter);

        var path = new QueryString(ApiRoutes.AuditLogs)
            .Add(QueryNames.TargetType, filter.TargetType)
            .Add(QueryNames.TargetId, filter.TargetId)
            .Add(QueryNames.AdminUserId, filter.AdminUserId)
            .Add(QueryNames.Page, page)
            .Add(QueryNames.PageSize, pageSize);
        return HttpCalls.GetAsync<PagedResponse<AuditLogEntry>>(http, path.ToString(), cancellationToken);
    }

    public Task<ApiResult<HangfireSessionResponse>> CreateHangfireSessionAsync(CancellationToken cancellationToken) =>
        HttpCalls.PostAsync<HangfireSessionResponse>(http, ApiRoutes.HangfireSession, null, cancellationToken);

    /// <summary>AD3: revokes the session's refresh token family. Idempotent.</summary>
    public Task<ApiResult<NoContent>> LogoutAsync(string refreshToken, CancellationToken cancellationToken) =>
        HttpCalls.PostAsync<NoContent>(http, ApiRoutes.Logout, new RefreshTokenRequest(refreshToken), cancellationToken);
}

/// <summary>AD20 filters; each one is optional.</summary>
public sealed record AuditLogFilter(string? TargetType, Guid? TargetId, Guid? AdminUserId);

/// <summary>Query parameter names of contract §11.3.</summary>
public static class QueryNames
{
    public const string Search = "search";
    public const string Status = "status";
    public const string Page = "page";
    public const string PageSize = "pageSize";
    public const string From = "from";
    public const string To = "to";
    public const string ShopId = "shopId";
    public const string TargetType = "targetType";
    public const string TargetId = "targetId";
    public const string AdminUserId = "adminUserId";
}
