using System.Net;
using System.Net.Http.Headers;
using Stockly.Api.IntegrationTests.Support;

namespace Stockly.Api.IntegrationTests.Admin;

/// <summary>Contract AD4–AD10 and AD20 on the real host: suspension, quota override and reset, subscriptions, audit.</summary>
[Collection(ApiCollectionDefinition.Name)]
public sealed class AdminShopEndpointTests(ApiFixture fixture)
{
    [Fact]
    public async Task Suspend_RevokesSessionsNextCallIsShopSuspended_RepeatWritesNoSecondAuditRow()
    {
        await using var factory = fixture.CreateFactory();
        using var client = factory.CreateClient();
        var shop = await AuthApi.RegisterAsync(client);
        var admin = await AdminApi.LoginAsync(client);

        var suspend = await AdminApi.PostAsync(client, $"{AdminApi.Shop(shop.ShopId)}/suspend", new { reason = "unpaid invoice" }, admin.AccessToken);
        var repeat = await AdminApi.PostAsync(client, $"{AdminApi.Shop(shop.ShopId)}/suspend", new { reason = "again" }, admin.AccessToken);
        var me = await AuthApi.MeAsync(client, shop.AccessToken);
        var refresh = await AuthApi.RefreshAsync(client, shop.RefreshToken);

        suspend.Status.Should().Be(HttpStatusCode.NoContent, suspend.Raw);
        repeat.Status.Should().Be(HttpStatusCode.NoContent);
        me.Status.Should().Be(HttpStatusCode.Forbidden);
        me.Code.Should().Be("Shop.Suspended");
        refresh.Status.Should().Be(HttpStatusCode.Unauthorized, "the shop's refresh tokens were revoked");
        (await AuditCountAsync(shop.ShopId, "Shop.Suspend")).Should().Be(1);
        (await fixture.ScalarAsync<string>("SELECT suspended_reason FROM shops WHERE id = @p0", shop.ShopId)).Should().Be("unpaid invoice");
        (await fixture.ScalarAsync<Guid>("SELECT admin_user_id FROM admin_audit_logs WHERE target_id = @p0", shop.ShopId)).Should().Be(admin.UserId);
    }

    [Fact]
    public async Task Activate_SuspendedShop_SignsInAgain_RepeatIsIdempotent()
    {
        await using var factory = fixture.CreateFactory();
        using var client = factory.CreateClient();
        var shop = await AuthApi.RegisterAsync(client);
        var admin = await AdminApi.LoginAsync(client);
        await AdminApi.PostAsync(client, $"{AdminApi.Shop(shop.ShopId)}/suspend", new { reason = "unpaid" }, admin.AccessToken);

        var activate = await AdminApi.PostAsync(client, $"{AdminApi.Shop(shop.ShopId)}/activate", new { reason = "paid" }, admin.AccessToken);
        var repeat = await AdminApi.PostAsync(client, $"{AdminApi.Shop(shop.ShopId)}/activate", new { reason = "paid" }, admin.AccessToken);
        var login = await AuthApi.LoginAsync(client, shop.Email);

        activate.Status.Should().Be(HttpStatusCode.NoContent);
        repeat.Status.Should().Be(HttpStatusCode.NoContent);
        login.ShopId.Should().Be(shop.ShopId);
        (await AuditCountAsync(shop.ShopId, "Shop.Activate")).Should().Be(1);
    }

    [Fact]
    public async Task Writes_BlankReason_400ReasonRequired_UnknownShop_404()
    {
        await using var factory = fixture.CreateFactory();
        using var client = factory.CreateClient();
        var shop = await AuthApi.RegisterAsync(client);
        var admin = await AdminApi.LoginAsync(client);
        var unknown = Guid.CreateVersion7();

        var blank = await AdminApi.PostAsync(client, $"{AdminApi.Shop(shop.ShopId)}/suspend", new { reason = "  " }, admin.AccessToken);
        var tooLong = await AdminApi.PostAsync(client, $"{AdminApi.Shop(shop.ShopId)}/suspend", new { reason = new string('x', 501) }, admin.AccessToken);
        var missingShop = await AdminApi.PostAsync(client, $"{AdminApi.Shop(unknown)}/quota/reset", new { reason = "x" }, admin.AccessToken);
        var missingDetail = await AdminApi.GetAsync(client, AdminApi.Shop(unknown), admin.AccessToken);

        blank.Status.Should().Be(HttpStatusCode.BadRequest);
        blank.Code.Should().Be("Admin.ReasonRequired");
        tooLong.Code.Should().Be("Validation.Failed");
        missingShop.Status.Should().Be(HttpStatusCode.NotFound);
        missingShop.Code.Should().Be("Shop.NotFound");
        missingDetail.Code.Should().Be("Shop.NotFound");
        (await AuditCountAsync(shop.ShopId, "Shop.Suspend")).Should().Be(0);
    }

    [Fact]
    public async Task QuotaOverride_AppliesToTheNextScan_AndResetGivesTheQuotaBack()
    {
        await using var factory = fixture.CreateFactory();
        using var client = factory.CreateClient();
        var shop = await AuthApi.RegisterAsync(client);
        var admin = await AdminApi.LoginAsync(client);

        var set = await AdminApi.PutAsync(client, $"{AdminApi.Shop(shop.ShopId)}/quota-override", new { dailyScanQuota = 1, reason = "trial" }, admin.AccessToken);
        var first = await ScanAsync(client, shop.AccessToken);
        var second = await ScanAsync(client, shop.AccessToken);
        var reset = await AdminApi.PostAsync(client, $"{AdminApi.Shop(shop.ShopId)}/quota/reset", new { reason = "support" }, admin.AccessToken);
        var third = await ScanAsync(client, shop.AccessToken);

        set.Status.Should().Be(HttpStatusCode.OK, set.Raw);
        set.Body!["planDailyScanQuota"]!.GetValue<int>().Should().Be(30);
        set.Body["dailyScanQuotaOverride"]!.GetValue<int>().Should().Be(1);
        set.Body["effectiveDailyScanQuota"]!.GetValue<int>().Should().Be(1);
        first.Should().Be(HttpStatusCode.Accepted);
        second.Should().Be(HttpStatusCode.TooManyRequests, "the override replaced the plan's 30 scans");
        reset.Status.Should().Be(HttpStatusCode.NoContent);
        third.Should().Be(HttpStatusCode.Accepted, "the reset deleted today's counter");
        (await AuditCountAsync(shop.ShopId, "Shop.SetQuotaOverride")).Should().Be(1);
        (await AuditCountAsync(shop.ShopId, "Shop.ResetQuota")).Should().Be(1);
    }

    [Fact]
    public async Task QuotaOverride_OutOfRange_400_Null_RemovesIt()
    {
        await using var factory = fixture.CreateFactory();
        using var client = factory.CreateClient();
        var shop = await AuthApi.RegisterAsync(client);
        var admin = await AdminApi.LoginAsync(client);
        var path = $"{AdminApi.Shop(shop.ShopId)}/quota-override";

        var tooBig = await AdminApi.PutAsync(client, path, new { dailyScanQuota = 10_001, reason = "x" }, admin.AccessToken);
        await AdminApi.PutAsync(client, path, new { dailyScanQuota = 5, reason = "x" }, admin.AccessToken);
        var removed = await AdminApi.PutAsync(client, path, new { dailyScanQuota = (int?)null, reason = "x" }, admin.AccessToken);

        tooBig.Status.Should().Be(HttpStatusCode.BadRequest);
        tooBig.Code.Should().Be("Validation.Failed");
        removed.Status.Should().Be(HttpStatusCode.OK);
        removed.Body!["dailyScanQuotaOverride"].Should().BeNull();
        removed.Body["effectiveDailyScanQuota"]!.GetValue<int>().Should().Be(30);
    }

    [Fact]
    public async Task ListAndDetail_ShowTheShopWithOwnerSubscriptionAndMembers()
    {
        await using var factory = fixture.CreateFactory();
        using var client = factory.CreateClient();
        var marker = Guid.NewGuid().ToString("N")[..8];
        var email = $"owner-{marker}@example.com";
        var register = await AuthApi.PostAsync(client, AuthApi.Register, AuthApi.RegisterBody(email, shopName: $"صيدلية الشفاء {marker}"));
        var shopId = register.Body!["shop"]!["id"]!.GetValue<Guid>();
        var admin = await AdminApi.LoginAsync(client);

        var byName = await AdminApi.GetAsync(client, $"{AdminApi.Shops}?search=صيدليه الشفاء {marker}", admin.AccessToken);
        var byEmail = await AdminApi.GetAsync(client, $"{AdminApi.Shops}?search=OWNER-{marker}&status=Active&pageSize=5", admin.AccessToken);
        var detail = await AdminApi.GetAsync(client, AdminApi.Shop(shopId), admin.AccessToken, language: "en");
        var badPage = await AdminApi.GetAsync(client, $"{AdminApi.Shops}?pageSize=101", admin.AccessToken);

        byName.Status.Should().Be(HttpStatusCode.OK, byName.Raw);
        byName.Body!["totalCount"]!.GetValue<int>().Should().Be(1);
        var row = byName.Body["items"]![0]!;
        row["id"]!.GetValue<Guid>().Should().Be(shopId);
        row["ownerEmail"]!.GetValue<string>().Should().Be(email);
        row["planCode"]!.GetValue<string>().Should().Be("standard");
        row["status"]!.GetValue<string>().Should().Be("Active");
        row["scansToday"]!.GetValue<int>().Should().Be(0);
        byEmail.Body!["items"]!.AsArray().Select(item => item!["id"]!.GetValue<Guid>()).Should().Equal(shopId);
        detail.Status.Should().Be(HttpStatusCode.OK, detail.Raw);
        detail.Body!["members"]!.AsArray().Should().ContainSingle();
        detail.Body["members"]![0]!["role"]!.GetValue<string>().Should().Be("Owner");
        detail.Body["subscription"]!["planName"]!.GetValue<string>().Should().Be("Standard");
        detail.Body["counts"]!["products"]!.GetValue<int>().Should().Be(0);
        detail.Body["aiCostLast30DaysUsd"]!.GetValue<string>().Should().Be("0.00000000");
        badPage.Code.Should().Be("Validation.Failed");
    }

    [Fact]
    public async Task ChangeSubscription_NewPlanFromTodayCarryingTheOverride_InactivePlan400_UnknownPlan404()
    {
        await using var factory = fixture.CreateFactory();
        using var client = factory.CreateClient();
        var shop = await AuthApi.RegisterAsync(client);
        var admin = await AdminApi.LoginAsync(client);
        await AdminApi.PutAsync(client, $"{AdminApi.Shop(shop.ShopId)}/quota-override", new { dailyScanQuota = 9, reason = "x" }, admin.AccessToken);
        var code = $"pro-{Guid.NewGuid():N}"[..20];
        var plan = await AdminApi.PostAsync(
            client, AdminApi.Plans, new { code, name = "Pro", dailyScanQuota = 100, maxMembers = 5, monthlyPrice = "199.00", isActive = true }, admin.AccessToken);
        var planId = plan.Body!["id"]!.GetValue<Guid>();
        var inactiveCode = $"old-{Guid.NewGuid():N}"[..20];
        var inactive = await AdminApi.PostAsync(
            client, AdminApi.Plans, new { code = inactiveCode, name = "Old", dailyScanQuota = 1, maxMembers = 1, monthlyPrice = "0.00", isActive = false }, admin.AccessToken);
        var path = $"{AdminApi.Shop(shop.ShopId)}/subscription";

        var changed = await AdminApi.PutAsync(client, path, new { planId, endsOn = (string?)null, reason = "upgrade" }, admin.AccessToken);
        var toInactive = await AdminApi.PutAsync(client, path, new { planId = inactive.Body!["id"]!.GetValue<Guid>(), reason = "x" }, admin.AccessToken);
        var toUnknown = await AdminApi.PutAsync(client, path, new { planId = Guid.CreateVersion7(), reason = "x" }, admin.AccessToken);
        var pastEnd = await AdminApi.PutAsync(client, path, new { planId, endsOn = "2020-01-01", reason = "x" }, admin.AccessToken);

        changed.Status.Should().Be(HttpStatusCode.OK, changed.Raw);
        changed.Body!["planCode"]!.GetValue<string>().Should().Be(code);
        changed.Body["planDailyScanQuota"]!.GetValue<int>().Should().Be(100);
        changed.Body["dailyScanQuotaOverride"]!.GetValue<int>().Should().Be(9);
        changed.Body["effectiveDailyScanQuota"]!.GetValue<int>().Should().Be(9);
        (await fixture.ScalarAsync<long>("SELECT count(*) FROM shop_subscriptions WHERE shop_id = @p0", shop.ShopId)).Should().Be(2);
        (await fixture.ScalarAsync<long>("SELECT count(*) FROM shop_subscriptions WHERE shop_id = @p0 AND status = 1", shop.ShopId)).Should().Be(1);
        toInactive.Status.Should().Be(HttpStatusCode.BadRequest);
        toInactive.Code.Should().Be("Plan.Inactive");
        toUnknown.Status.Should().Be(HttpStatusCode.NotFound);
        toUnknown.Code.Should().Be("Plan.NotFound");
        pastEnd.Code.Should().Be("Validation.Failed");
        (await AuditCountAsync(shop.ShopId, "Shop.ChangeSubscription")).Should().Be(1);
    }

    [Fact]
    public async Task AuditLogs_FilteredByTarget_NewestFirstWithAdminEmailAndJson()
    {
        await using var factory = fixture.CreateFactory();
        using var client = factory.CreateClient();
        var shop = await AuthApi.RegisterAsync(client);
        var admin = await AdminApi.LoginAsync(client);
        await AdminApi.PostAsync(client, $"{AdminApi.Shop(shop.ShopId)}/suspend", new { reason = "first" }, admin.AccessToken);
        await AdminApi.PostAsync(client, $"{AdminApi.Shop(shop.ShopId)}/activate", new { reason = "second" }, admin.AccessToken);

        var logs = await AdminApi.GetAsync(client, $"{AdminApi.AuditLogs}?targetType=Shop&targetId={shop.ShopId}", admin.AccessToken);

        logs.Status.Should().Be(HttpStatusCode.OK, logs.Raw);
        var items = logs.Body!["items"]!.AsArray();
        items.Select(item => item!["action"]!.GetValue<string>()).Should().Equal("Shop.Activate", "Shop.Suspend");
        items[1]!["adminEmail"]!.GetValue<string>().Should().Be(AdminApi.Email);
        items[1]!["reason"]!.GetValue<string>().Should().Be("first");
        items[1]!["before"]!["status"]!.GetValue<string>().Should().Be("Active");
        items[1]!["after"]!["status"]!.GetValue<string>().Should().Be("Suspended");
    }

    private static async Task<HttpStatusCode> ScanAsync(HttpClient client, string token)
    {
        var form = new MultipartFormDataContent { { new StringContent(Guid.CreateVersion7().ToString()), "clientRequestId" } };
        var file = new ByteArrayContent(InvoiceApi.Jpeg);
        file.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        form.Add(file, "image", "invoice.jpg");
        var response = await AuthApi.SendAsync(
            client, new HttpRequestMessage(HttpMethod.Post, new Uri("/api/invoices/scan", UriKind.Relative)) { Content = form }, bearer: token);
        return response.Status;
    }

    private Task<long> AuditCountAsync(Guid shopId, string action) =>
        fixture.ScalarAsync<long>("SELECT count(*) FROM admin_audit_logs WHERE target_id = @p0 AND action = @p1", shopId, action);
}
