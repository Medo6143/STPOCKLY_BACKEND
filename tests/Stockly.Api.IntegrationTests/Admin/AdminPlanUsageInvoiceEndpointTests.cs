using System.Net;
using Stockly.Api.IntegrationTests.Support;
using StackExchange.Redis;
using Stockly.Application.Abstractions.Caching;
using Stockly.Domain.Platform;

namespace Stockly.Api.IntegrationTests.Admin;

/// <summary>Contract AD11–AD19 on the real host with PostgreSQL, Redis and MinIO.</summary>
[Collection(ApiCollectionDefinition.Name)]
public sealed class AdminPlanUsageInvoiceEndpointTests(ApiFixture fixture)
{
    [Fact]
    public async Task Plans_CreateListUpdate_NameInTheRequestLanguage_DuplicateCode409_Unknown404()
    {
        await using var factory = fixture.CreateFactory();
        using var client = factory.CreateClient();
        var admin = await AdminApi.LoginAsync(client);
        var code = $"gold-{Guid.NewGuid():N}"[..20];
        var body = new { code, name = "Gold", dailyScanQuota = 200, maxMembers = 10, monthlyPrice = "299.50", isActive = true };

        var created = await AdminApi.PostAsync(client, AdminApi.Plans, body, admin.AccessToken);
        var duplicate = await AdminApi.PostAsync(client, AdminApi.Plans, body, admin.AccessToken);
        var badCode = await AdminApi.PostAsync(client, AdminApi.Plans, body with { code = "Gold Plan" }, admin.AccessToken);
        var id = created.Body!["id"]!.GetValue<Guid>();
        var updated = await AdminApi.PutAsync(
            client, $"{AdminApi.Plans}/{id}", new { name = "Gold+", dailyScanQuota = 250, maxMembers = 12, monthlyPrice = "349.00", isActive = false }, admin.AccessToken);
        var unknown = await AdminApi.PutAsync(
            client, $"{AdminApi.Plans}/{Guid.CreateVersion7()}", new { name = "x", dailyScanQuota = 1, maxMembers = 1, monthlyPrice = "1.00", isActive = true }, admin.AccessToken);
        var arabic = await AdminApi.GetAsync(client, AdminApi.Plans, admin.AccessToken, language: "ar");
        var english = await AdminApi.GetAsync(client, AdminApi.Plans, admin.AccessToken, language: "en");

        created.Status.Should().Be(HttpStatusCode.Created, created.Raw);
        created.Body["monthlyPrice"]!.GetValue<string>().Should().Be("299.50");
        duplicate.Status.Should().Be(HttpStatusCode.Conflict);
        duplicate.Code.Should().Be("Plan.CodeTaken");
        badCode.Code.Should().Be("Validation.Failed");
        updated.Status.Should().Be(HttpStatusCode.OK, updated.Raw);
        updated.Body!["code"]!.GetValue<string>().Should().Be(code, "the code is immutable");
        updated.Body["isActive"]!.GetValue<bool>().Should().BeFalse();
        unknown.Code.Should().Be("Plan.NotFound");
        arabic.Body!.AsArray().Should().Contain(plan => plan!["code"]!.GetValue<string>() == "standard" && plan["name"]!.GetValue<string>() == "الأساسية");
        english.Body!.AsArray().Should().Contain(plan => plan!["code"]!.GetValue<string>() == "standard" && plan["name"]!.GetValue<string>() == "Standard");
        english.Body!.AsArray().First()!["isActive"]!.GetValue<bool>().Should().BeTrue("active plans come first");
        (await fixture.ScalarAsync<long>("SELECT count(*) FROM admin_audit_logs WHERE target_id = @p0", id)).Should().Be(2);
    }

    [Fact]
    public async Task DailyUsage_EveryDayOfTheRangeZeroFilled_RangeOver92Days400()
    {
        await using var factory = fixture.CreateFactory();
        using var client = factory.CreateClient();
        var admin = await AdminApi.LoginAsync(client);

        var days = await AdminApi.GetAsync(client, "/api/admin/ai-usage/daily?from=2035-02-01&to=2035-02-03", admin.AccessToken);
        var tooLong = await AdminApi.GetAsync(client, "/api/admin/ai-usage/daily?from=2035-01-01&to=2035-04-04", admin.AccessToken);
        var defaults = await AdminApi.GetAsync(client, "/api/admin/ai-usage/daily", admin.AccessToken);
        var byShop = await AdminApi.GetAsync(client, "/api/admin/ai-usage/by-shop?from=2035-02-01&to=2035-02-03", admin.AccessToken);
        var byModel = await AdminApi.GetAsync(client, "/api/admin/ai-usage/by-model?from=2035-02-01&to=2035-02-03", admin.AccessToken);

        days.Status.Should().Be(HttpStatusCode.OK, days.Raw);
        days.Body!.AsArray().Select(day => day!["date"]!.GetValue<string>()).Should().Equal("2035-02-01", "2035-02-02", "2035-02-03");
        days.Body!.AsArray().Should().OnlyContain(day => day!["calls"]!.GetValue<int>() == 0 && day["costUsd"]!.GetValue<string>() == "0.00000000");
        tooLong.Status.Should().Be(HttpStatusCode.BadRequest);
        tooLong.Body!["errors"]!["to"].Should().NotBeNull();
        defaults.Body!.AsArray().Should().HaveCount(30);
        byShop.Body!["totalCount"]!.GetValue<int>().Should().Be(0);
        byModel.Body!.AsArray().Should().BeEmpty();
    }

    [Fact]
    public async Task FailedInvoices_DetailWithSignedImage_RerunOnlyFailed()
    {
        await using var factory = fixture.CreateFactory();
        using var client = factory.CreateClient();
        var shop = await AuthApi.RegisterAsync(client);
        var invoice = await InvoiceApi.ScanAsync(client, shop.AccessToken);
        await fixture.ExecuteAsync(
            "UPDATE invoices SET status = 4, failure_reason = 'AiUnavailable', processed_at = now(), raw_ai_json = '\"oops\"'::jsonb WHERE id = @p0", invoice);
        var admin = await AdminApi.LoginAsync(client);

        var failed = await AdminApi.GetAsync(client, $"{AdminApi.FailedInvoices}?shopId={shop.ShopId}", admin.AccessToken);
        var detail = await AdminApi.GetAsync(client, AdminApi.Invoice(invoice), admin.AccessToken);
        var rerun = await AdminApi.PostAsync(client, $"{AdminApi.Invoice(invoice)}/rerun", new { reason = "AI back" }, admin.AccessToken);
        var again = await AdminApi.PostAsync(client, $"{AdminApi.Invoice(invoice)}/rerun", new { reason = "AI back" }, admin.AccessToken);
        var unknown = await AdminApi.PostAsync(client, $"{AdminApi.Invoice(Guid.CreateVersion7())}/rerun", new { reason = "x" }, admin.AccessToken);
        var missing = await AdminApi.GetAsync(client, AdminApi.Invoice(Guid.CreateVersion7()), admin.AccessToken);
        var shopView = await InvoiceApi.GetAsync(client, InvoiceApi.Invoice(invoice), shop.AccessToken);

        failed.Status.Should().Be(HttpStatusCode.OK, failed.Raw);
        failed.Body!["items"]!.AsArray().Should().ContainSingle().Which!["invoiceId"]!.GetValue<Guid>().Should().Be(invoice);
        failed.Body["items"]![0]!["failureReason"]!.GetValue<string>().Should().Be("AiUnavailable");
        detail.Status.Should().Be(HttpStatusCode.OK, detail.Raw);
        detail.Body!["invoice"]!["status"]!.GetValue<string>().Should().Be("Failed");
        detail.Body["shopId"]!.GetValue<Guid>().Should().Be(shop.ShopId);
        detail.Body["rawAiJson"]!.GetValue<string>().Should().Be("\"oops\"");
        var imageUrl = detail.Body["imageUrl"]!.GetValue<string>();
        using (var http = new HttpClient())
        {
            (await http.GetByteArrayAsync(new Uri(imageUrl))).Should().Equal(InvoiceApi.Jpeg);
        }

        rerun.Status.Should().Be(HttpStatusCode.Accepted, rerun.Raw);
        rerun.Body!["status"]!.GetValue<string>().Should().Be("Processing");
        rerun.Body["failureReason"].Should().BeNull();
        again.Status.Should().Be(HttpStatusCode.Conflict);
        again.Code.Should().Be("Invoice.NotRerunnable");
        unknown.Code.Should().Be("Invoice.NotFound");
        missing.Status.Should().Be(HttpStatusCode.NotFound);
        shopView.Body!["status"]!.GetValue<string>().Should().Be("Processing", "the status cache was updated after the commit");
        (await fixture.ScalarAsync<long>("SELECT count(*) FROM admin_audit_logs WHERE target_id = @p0 AND action = 'Invoice.Rerun'", invoice))
            .Should().Be(1);
    }

    [Fact]
    public async Task Rerun_EndToEnd_BypassesTheAiCacheCallsTheAiAndChargesNoQuota()
    {
        await using var factory = fixture.CreateFactory(jobs: StocklyApiFactory.JobMode.Run);
        using var client = factory.CreateClient();
        var shop = await AuthApi.RegisterAsync(client);
        var image = Enumerable.Range(0, 32).Select(i => (byte)i).Prepend((byte)0xE0).Prepend((byte)0xFF).Prepend((byte)0xD8).Prepend((byte)0xFF).ToArray();

        // A first scan of the photo is extracted: its result is now in the AI cache and reusable from the database.
        var first = await InvoiceApi.ScanAsync(client, shop.AccessToken, image);
        (await InvoiceApi.PollUntilProcessedAsync(client, first, shop.AccessToken, TimeSpan.FromSeconds(30))).Body!["status"]!
            .GetValue<string>().Should().Be("NeedsReview");

        // A second scan of the same photo is served from the cache; the test then makes it a failed scan (no lines).
        var second = await InvoiceApi.ScanAsync(client, shop.AccessToken, image);
        (await InvoiceApi.PollUntilProcessedAsync(client, second, shop.AccessToken, TimeSpan.FromSeconds(30))).Body!["status"]!
            .GetValue<string>().Should().Be("NeedsReview");
        var callsBefore = factory.Extractor.Calls;
        await fixture.ExecuteAsync("DELETE FROM invoice_items WHERE invoice_id = @p0", second);
        await fixture.ExecuteAsync("UPDATE invoices SET status = 4, failure_reason = 'AiOutputInvalid' WHERE id = @p0", second);
        var quotaBefore = await QuotaUsedAsync(shop.ShopId);
        quotaBefore.Should().Be(2, "each scan took one scan from today's quota");
        var admin = await AdminApi.LoginAsync(client);

        var rerun = await AdminApi.PostAsync(client, $"{AdminApi.Invoice(second)}/rerun", new { reason = "check" }, admin.AccessToken);
        var processed = await InvoiceApi.PollUntilProcessedAsync(client, second, shop.AccessToken, TimeSpan.FromSeconds(30));

        rerun.Status.Should().Be(HttpStatusCode.Accepted, rerun.Raw);
        processed.Body!["status"]!.GetValue<string>().Should().Be("NeedsReview");
        factory.Extractor.Calls.Should().Be(callsBefore + 1, "the re-run skips the cache and the earlier extraction");
        (await QuotaUsedAsync(shop.ShopId)).Should().Be(quotaBefore, "a re-run is not charged to the quota");
        (await fixture.ScalarAsync<long>(
                "SELECT count(*) FROM ai_usage_records WHERE invoice_id = @p0 AND NOT cache_hit AND outcome = @p1", second, (short)AiCallOutcome.Succeeded))
            .Should().Be(1, "every AI call still writes a usage row (rule 18)");
    }

    // The shop's Redis scan counter of today (Africa/Cairo), as the quota check reads it.
    private async Task<long> QuotaUsedAsync(Guid shopId)
    {
        var cairo = TimeZoneInfo.FindSystemTimeZoneById("Africa/Cairo");
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, cairo).DateTime);
        await using var redis = await ConnectionMultiplexer.ConnectAsync(fixture.RedisConnectionString);
        var value = await redis.GetDatabase().StringGetAsync(CacheKeys.ScanQuota(shopId, today));
        return value.IsNull ? 0 : (long)value;
    }
}
