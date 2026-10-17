using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using Stockly.Api.IntegrationTests.Support;

namespace Stockly.Api.IntegrationTests.Invoices;

/// <summary>Contract I1 <c>POST /api/invoices/scan</c> on the real host with PostgreSQL, Redis and MinIO.</summary>
[Collection(ApiCollectionDefinition.Name)]
public sealed class ScanEndpointTests(ApiFixture fixture)
{
    private const string Scan = "/api/invoices/scan";

    private static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46, 0x00, 0x01];

    [Fact]
    public async Task Scan_NewImage_202ProcessingWithLocationAndStoredImage()
    {
        await using var factory = fixture.CreateFactory();
        using var client = factory.CreateClient();
        var session = await AuthApi.RegisterAsync(client);
        var clientRequestId = Guid.CreateVersion7();

        var response = await ScanAsync(client, session.AccessToken, clientRequestId);

        response.Status.Should().Be(HttpStatusCode.Accepted, response.Raw);
        var id = response.Body!["id"]!.GetValue<Guid>();
        response.Location.Should().Be(new Uri($"/api/invoices/{id}", UriKind.Relative));
        response.Body["status"]!.GetValue<string>().Should().Be("Processing");
        response.Body["itemCount"]!.GetValue<int>().Should().Be(0);
        response.Body["warnings"]!.AsArray().Should().BeEmpty();
        response.Body["totalAmount"].Should().BeNull();

        var key = await fixture.ScalarAsync<string>("SELECT image_object_key FROM invoices WHERE id = @p0", id);
        key.Should().StartWith($"invoices/{session.ShopId}/").And.EndWith($"/{id}.jpg");
        (await fixture.ScalarAsync<Guid>("SELECT client_request_id FROM invoices WHERE id = @p0", id)).Should().Be(clientRequestId);
    }

    [Fact]
    public async Task Scan_RepeatedClientRequestId_200WithTheSameInvoice()
    {
        await using var factory = fixture.CreateFactory();
        using var client = factory.CreateClient();
        var session = await AuthApi.RegisterAsync(client);
        var clientRequestId = Guid.CreateVersion7();
        var first = await ScanAsync(client, session.AccessToken, clientRequestId);

        var repeat = await ScanAsync(client, session.AccessToken, clientRequestId);

        repeat.Status.Should().Be(HttpStatusCode.OK, repeat.Raw);
        repeat.Body!["id"]!.GetValue<Guid>().Should().Be(first.Body!["id"]!.GetValue<Guid>());
        (await fixture.ScalarAsync<long>("SELECT count(*) FROM invoices WHERE client_request_id = @p0", clientRequestId)).Should().Be(1);
    }

    [Fact]
    public async Task Scan_SameClientRequestIdInAnotherShop_CreatesItsOwnInvoice()
    {
        await using var factory = fixture.CreateFactory();
        using var client = factory.CreateClient();
        var shopA = await AuthApi.RegisterAsync(client);
        var shopB = await AuthApi.RegisterAsync(client);
        var clientRequestId = Guid.CreateVersion7();
        var first = await ScanAsync(client, shopA.AccessToken, clientRequestId);

        var other = await ScanAsync(client, shopB.AccessToken, clientRequestId);

        other.Status.Should().Be(HttpStatusCode.Accepted, "another shop's invoice is invisible (rule 10)");
        other.Body!["id"]!.GetValue<Guid>().Should().NotBe(first.Body!["id"]!.GetValue<Guid>());
    }

    [Fact]
    public async Task Scan_NoImage_400ImageRequired()
    {
        await using var factory = fixture.CreateFactory();
        using var client = factory.CreateClient();
        var session = await AuthApi.RegisterAsync(client);

        var response = await ScanAsync(client, session.AccessToken, Guid.CreateVersion7(), image: null);

        response.Status.Should().Be(HttpStatusCode.BadRequest, response.Raw);
        response.Code.Should().Be("Invoice.ImageRequired");
    }

    [Fact]
    public async Task Scan_ImageThatIsNotAJpeg_400UnsupportedImageType()
    {
        await using var factory = fixture.CreateFactory();
        using var client = factory.CreateClient();
        var session = await AuthApi.RegisterAsync(client);

        var response = await ScanAsync(client, session.AccessToken, Guid.CreateVersion7(), image: "GIF89a....."u8.ToArray());

        response.Status.Should().Be(HttpStatusCode.BadRequest, response.Raw);
        response.Code.Should().Be("Invoice.UnsupportedImageType");
    }

    [Fact]
    public async Task Scan_MissingClientRequestId_400ValidationFailed()
    {
        await using var factory = fixture.CreateFactory();
        using var client = factory.CreateClient();
        var session = await AuthApi.RegisterAsync(client);

        var response = await ScanAsync(client, session.AccessToken, clientRequestId: null);

        response.Status.Should().Be(HttpStatusCode.BadRequest, response.Raw);
        response.Code.Should().Be("Validation.Failed");
        response.Body!["errors"]!["clientRequestId"].Should().NotBeNull();
    }

    [Fact]
    public async Task Scan_ImageOverEightMegabytes_413ImageTooLarge()
    {
        await using var factory = fixture.CreateFactory();
        using var client = factory.CreateClient();
        var session = await AuthApi.RegisterAsync(client);
        var image = new byte[(8 * 1024 * 1024) + 1];
        Jpeg.CopyTo(image, 0);

        var response = await ScanAsync(client, session.AccessToken, Guid.CreateVersion7(), image);

        response.Status.Should().Be(HttpStatusCode.RequestEntityTooLarge, response.Raw);
        response.Code.Should().Be("Invoice.ImageTooLarge");
    }

    [Fact]
    public async Task Scan_BodyOverTheRequestLimit_413ImageTooLarge()
    {
        await using var factory = fixture.CreateFactory();
        using var client = factory.CreateClient();
        var session = await AuthApi.RegisterAsync(client);
        var image = new byte[10 * 1024 * 1024];
        Jpeg.CopyTo(image, 0);

        var response = await ScanAsync(client, session.AccessToken, Guid.CreateVersion7(), image);

        response.Status.Should().Be(HttpStatusCode.RequestEntityTooLarge, response.Raw);
        response.Code.Should().Be("Invoice.ImageTooLarge");
    }

    [Fact]
    public async Task Scan_MoreThanTenInAMinute_429RateLimitedWithRetryAfter()
    {
        // Near the real time (never ahead of it), so the access token is valid; the start of a minute.
        var now = DateTimeOffset.UtcNow;
        var clock = new TestClock(new DateTimeOffset(now.Year, now.Month, now.Day, now.Hour, now.Minute, 0, TimeSpan.Zero));
        await using var factory = fixture.CreateFactory(clock: clock);
        using var client = factory.CreateClient();
        var session = await AuthApi.RegisterAsync(client);

        for (var i = 0; i < 10; i++)
        {
            (await ScanAsync(client, session.AccessToken, Guid.CreateVersion7())).Status.Should().Be(HttpStatusCode.Accepted);
        }

        var limited = await ScanAsync(client, session.AccessToken, Guid.CreateVersion7());

        limited.Status.Should().Be(HttpStatusCode.TooManyRequests, limited.Raw);
        limited.Code.Should().Be("Scan.RateLimited");
        limited.RetryAfter.Should().Be("60");
    }

    [Fact]
    public async Task Scan_DailyQuotaUsedUp_429WithRetryAfterUntilCairoMidnight()
    {
        // Near the real time, so the access token is valid; whole seconds, so Retry-After is exact.
        var now = DateTimeOffset.UtcNow;
        var clock = new TestClock(new DateTimeOffset(now.Ticks - (now.Ticks % TimeSpan.TicksPerSecond), TimeSpan.Zero));
        await using var factory = fixture.CreateFactory(clock: clock);
        using var client = factory.CreateClient();
        var session = await AuthApi.RegisterAsync(client);
        await GiveShopAPlanAsync(session.ShopId, dailyScanQuota: 1);
        (await ScanAsync(client, session.AccessToken, Guid.CreateVersion7())).Status.Should().Be(HttpStatusCode.Accepted);

        var refused = await ScanAsync(client, session.AccessToken, Guid.CreateVersion7());

        refused.Status.Should().Be(HttpStatusCode.TooManyRequests, refused.Raw);
        refused.Code.Should().Be("Scan.DailyQuotaExceeded");
        refused.RetryAfter.Should().Be(SecondsToNextCairoMidnight(clock.UtcNow).ToString(CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task Scan_RedisDown_StillAcceptedWithThePostgresQuotaCount()
    {
        await using var factory = fixture.CreateFactory(redis: ApiFixture.UnreachableRedis);
        using var client = factory.CreateClient();
        var session = await AuthApi.RegisterAsync(client);
        await GiveShopAPlanAsync(session.ShopId, dailyScanQuota: 1);

        var first = await ScanAsync(client, session.AccessToken, Guid.CreateVersion7());
        var second = await ScanAsync(client, session.AccessToken, Guid.CreateVersion7());

        first.Status.Should().Be(HttpStatusCode.Accepted, "Redis failure never fails a request (rule 25)");
        second.Code.Should().Be("Scan.DailyQuotaExceeded", "the quota falls back to counting today's invoices");
    }

    [Fact]
    public async Task Scan_NoToken_401()
    {
        await using var factory = fixture.CreateFactory();
        using var client = factory.CreateClient();

        var response = await ScanAsync(client, bearer: null, Guid.CreateVersion7());

        response.Status.Should().Be(HttpStatusCode.Unauthorized);
        response.Code.Should().Be("Auth.Unauthenticated");
    }

    [Fact]
    public async Task Scan_WithJobsRunning_InvoiceBecomesNeedsReviewWithLinesAndUsage()
    {
        await using var factory = fixture.CreateFactory(jobs: StocklyApiFactory.JobMode.Run);
        using var client = factory.CreateClient();
        var session = await AuthApi.RegisterAsync(client);

        var response = await ScanAsync(client, session.AccessToken, Guid.CreateVersion7());
        var id = response.Body!["id"]!.GetValue<Guid>();

        var status = await WaitForStatusAsync(id, TimeSpan.FromSeconds(60));
        status.Should().Be((short)2, "the job turns the scan into NeedsReview");
        (await fixture.ScalarAsync<long>("SELECT count(*) FROM invoice_items WHERE invoice_id = @p0", id)).Should().Be(1);
        (await fixture.ScalarAsync<long>("SELECT count(*) FROM ai_usage_records WHERE invoice_id = @p0", id)).Should().Be(1);
        factory.Extractor.Calls.Should().BeGreaterThanOrEqualTo(1, "the server also drains jobs queued by earlier tests");
    }

    private static long SecondsToNextCairoMidnight(DateTimeOffset utcNow)
    {
        var cairo = TimeZoneInfo.FindSystemTimeZoneById("Africa/Cairo");
        var nextMidnight = TimeZoneInfo.ConvertTime(utcNow, cairo).Date.AddDays(1);
        while (cairo.IsInvalidTime(nextMidnight))
        {
            nextMidnight = nextMidnight.AddHours(1);
        }

        return (long)Math.Ceiling((TimeZoneInfo.ConvertTimeToUtc(nextMidnight, cairo) - utcNow.UtcDateTime).TotalSeconds);
    }

    private async Task<short> WaitForStatusAsync(Guid invoiceId, TimeSpan timeout)
    {
        var watch = Stopwatch.StartNew();
        short status;
        do
        {
            status = await fixture.ScalarAsync<short>("SELECT status FROM invoices WHERE id = @p0", invoiceId);
            if (status != 1)
            {
                return status;
            }

            await Task.Delay(200);
        }
        while (watch.Elapsed < timeout);

        return status;
    }

    private async Task GiveShopAPlanAsync(Guid shopId, int dailyScanQuota)
    {
        var planId = Guid.CreateVersion7();
        await fixture.ExecuteAsync(
            "INSERT INTO plans (id, code, name_ar, name_en, daily_scan_quota, max_members, monthly_price, is_active) " +
            "VALUES (@p0, @p1, 'اختبار', 'Test', @p2, 3, 0, true)",
            planId,
            $"test-{planId:N}",
            dailyScanQuota);
        await fixture.ExecuteAsync("UPDATE shop_subscriptions SET plan_id = @p0 WHERE shop_id = @p1", planId, shopId);
    }

    private static Task<ApiResponse> ScanAsync(HttpClient client, string? bearer, Guid? clientRequestId, byte[]? image = null, bool withImage = true)
    {
        var form = new MultipartFormDataContent();
        if (clientRequestId is { } id)
        {
            form.Add(new StringContent(id.ToString()), "clientRequestId");
        }

        var bytes = image ?? (withImage ? Jpeg : null);
        if (bytes is not null)
        {
            var file = new ByteArrayContent(bytes);
            file.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
            form.Add(file, "image", "invoice.jpg");
        }

        var request = new HttpRequestMessage(HttpMethod.Post, new Uri(Scan, UriKind.Relative)) { Content = form };
        return AuthApi.SendAsync(client, request, bearer: bearer);
    }

    private static Task<ApiResponse> ScanAsync(HttpClient client, string bearer, Guid clientRequestId, byte[]? image) =>
        image is null
            ? ScanAsync(client, bearer, (Guid?)clientRequestId, image: null, withImage: false)
            : ScanAsync(client, bearer, (Guid?)clientRequestId, image);
}
