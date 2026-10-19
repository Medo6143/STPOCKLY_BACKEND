using System.Net;
using System.Net.Http.Headers;
using Stockly.Api.IntegrationTests.Support;

namespace Stockly.Api.IntegrationTests.Billing;

/// <summary>
/// Contract §12 (B1–B6, the Paymob callback, AD21–AD22) on the real host with PostgreSQL and a fake Paymob: owner-only
/// rows, wrong tenant 404, idempotent checkout, provider failure 502 with nothing stored, the signed callback that buys
/// the month (and its repeat), and the admin reads. Paymob is never called.
/// </summary>
[Collection(ApiCollectionDefinition.Name)]
public sealed class BillingEndpointTests(ApiFixture fixture)
{
    private const string Plans = "/api/billing/plans";
    private const string Subscription = "/api/billing/subscription";
    private const string Checkout = "/api/billing/checkout";
    private const string Payments = "/api/billing/payments";
    private const string Callback = "/api/payments/paymob/callback";
    private const string AdminPayments = "/api/admin/payments";

    [Fact]
    public async Task PlansAndSubscription_NewShop_DefaultFreePlanIsCurrent()
    {
        using var paymob = new FakePaymobApi();
        await using var factory = Factory(paymob);
        using var client = factory.CreateClient();
        var session = await AuthApi.RegisterAsync(client);

        var plans = await ProductApi.GetAsync(client, Plans, session.AccessToken, language: "en");
        var subscription = await ProductApi.GetAsync(client, Subscription, session.AccessToken, language: "en");

        plans.Status.Should().Be(HttpStatusCode.OK, plans.Raw);
        var standard = plans.Body!.AsArray().Single(plan => plan!["code"]!.GetValue<string>() == "standard")!;
        standard["isFree"]!.GetValue<bool>().Should().BeTrue();
        standard["isCurrent"]!.GetValue<bool>().Should().BeTrue();
        standard["monthlyPrice"]!.GetValue<string>().Should().Be("0.00");
        standard["name"]!.GetValue<string>().Should().Be("Standard");
        plans.Body!.AsArray()[0]!["isFree"]!.GetValue<bool>().Should().BeTrue("free plans come first");
        subscription.Status.Should().Be(HttpStatusCode.OK, subscription.Raw);
        subscription.Body!["plan"]!["code"]!.GetValue<string>().Should().Be("standard");
        subscription.Body!["endsOn"].Should().BeNull();
        subscription.Body!["memberCount"]!.GetValue<int>().Should().Be(1);
        subscription.Body!["scansToday"]!.GetValue<int>().Should().Be(0);
        subscription.Body!["effectiveDailyScanQuota"]!.GetValue<int>().Should().Be(30);
        subscription.Body!["pendingPayment"].Should().BeNull();
    }

    [Fact]
    public async Task Checkout_Card_201ThenRepeat200ThenReadAndList()
    {
        using var paymob = new FakePaymobApi();
        await using var factory = Factory(paymob);
        using var client = factory.CreateClient();
        var plan = await PaidPlanAsync(client, "149.75");
        var session = await AuthApi.RegisterAsync(client);
        var body = new { clientRequestId = Guid.CreateVersion7(), planId = plan, method = "Card" };

        var created = await ProductApi.PostAsync(client, Checkout, body, session.AccessToken);
        var repeat = await ProductApi.PostAsync(client, Checkout, body, session.AccessToken);

        created.Status.Should().Be(HttpStatusCode.Created, created.Raw);
        var id = created.Body!["id"]!.GetValue<Guid>();
        created.Location!.ToString().Should().Be($"/api/billing/payments/{id}");
        created.Body!["status"]!.GetValue<string>().Should().Be("Pending");
        created.Body!["amount"]!.GetValue<string>().Should().Be("149.75", "the plan's price, never a client amount");
        created.Body!["currency"]!.GetValue<string>().Should().Be("EGP");
        created.Body!["checkoutUrl"]!.GetValue<string>().Should().StartWith("https://eg.checkout.paymob.test/?publicKey=");
        created.Body!["referenceNumber"].Should().BeNull();
        created.Body!["expiresAt"].Should().NotBeNull();
        repeat.Status.Should().Be(HttpStatusCode.OK, repeat.Raw);
        repeat.Raw.Should().Be(created.Raw);
        paymob.IntentionCount.Should().Be(1, "a repeat never calls the provider again");

        var read = await ProductApi.GetAsync(client, $"{Payments}/{id}", session.AccessToken);
        var list = await ProductApi.GetAsync(client, Payments, session.AccessToken);
        var summary = await ProductApi.GetAsync(client, Subscription, session.AccessToken);
        read.Raw.Should().Be(created.Raw);
        list.Body!["totalCount"]!.GetValue<int>().Should().Be(1);
        summary.Body!["pendingPayment"]!["id"]!.GetValue<Guid>().Should().Be(id);
    }

    [Fact]
    public async Task Checkout_KioskAndWallet_ReferenceOrWalletPage()
    {
        using var paymob = new FakePaymobApi();
        await using var factory = Factory(paymob);
        using var client = factory.CreateClient();
        var plan = await PaidPlanAsync(client, "50.00");
        var kioskShop = await AuthApi.RegisterAsync(client);
        var walletShop = await AuthApi.RegisterAsync(client);

        var kiosk = await ProductApi.PostAsync(client, Checkout, new { clientRequestId = Guid.CreateVersion7(), planId = plan, method = "Kiosk" }, kioskShop.AccessToken);
        var wallet = await ProductApi.PostAsync(
            client, Checkout, new { clientRequestId = Guid.CreateVersion7(), planId = plan, method = "Wallet", walletPhone = "01012345678" }, walletShop.AccessToken);

        kiosk.Status.Should().Be(HttpStatusCode.Created, kiosk.Raw);
        kiosk.Body!["referenceNumber"]!.GetValue<string>().Should().Be("7788991");
        kiosk.Body!["checkoutUrl"].Should().BeNull();
        wallet.Status.Should().Be(HttpStatusCode.Created, wallet.Raw);
        wallet.Body!["checkoutUrl"]!.GetValue<string>().Should().Be("https://accept.paymob.test/wallet/otp");
    }

    [Fact]
    public async Task Checkout_ErrorCodes()
    {
        using var paymob = new FakePaymobApi();
        await using var factory = Factory(paymob);
        using var client = factory.CreateClient();
        var plan = await PaidPlanAsync(client, "99.00");
        var session = await AuthApi.RegisterAsync(client);
        var staffToken = TestJwt.Mint(session.UserId, session.ShopId, "Staff");

        var staff = await ProductApi.PostAsync(client, Checkout, new { clientRequestId = Guid.CreateVersion7(), planId = plan, method = "Card" }, staffToken);
        var free = await ProductApi.PostAsync(client, Checkout, new { clientRequestId = Guid.CreateVersion7(), planId = DefaultPlanId, method = "Card" }, session.AccessToken);
        var unknown = await ProductApi.PostAsync(client, Checkout, new { clientRequestId = Guid.CreateVersion7(), planId = Guid.CreateVersion7(), method = "Card" }, session.AccessToken);
        var noPhone = await ProductApi.PostAsync(client, Checkout, new { clientRequestId = Guid.CreateVersion7(), planId = plan, method = "Wallet" }, session.AccessToken);
        var noId = await ProductApi.PostAsync(client, Checkout, new { planId = plan, method = "Card" }, session.AccessToken);
        var first = await ProductApi.PostAsync(client, Checkout, new { clientRequestId = Guid.CreateVersion7(), planId = plan, method = "Card" }, session.AccessToken);
        var second = await ProductApi.PostAsync(client, Checkout, new { clientRequestId = Guid.CreateVersion7(), planId = plan, method = "Kiosk" }, session.AccessToken);

        staff.Status.Should().Be(HttpStatusCode.Forbidden);
        staff.Code.Should().Be("Auth.Forbidden");
        free.Status.Should().Be(HttpStatusCode.BadRequest);
        free.Code.Should().Be("Billing.FreePlan");
        unknown.Status.Should().Be(HttpStatusCode.NotFound);
        unknown.Code.Should().Be("Plan.NotFound");
        noPhone.Status.Should().Be(HttpStatusCode.BadRequest);
        noPhone.Code.Should().Be("Validation.Failed");
        noPhone.Body!["errors"]!["walletPhone"].Should().NotBeNull();
        noId.Code.Should().Be("Validation.Failed");
        first.Status.Should().Be(HttpStatusCode.Created, first.Raw);
        second.Status.Should().Be(HttpStatusCode.Conflict);
        second.Code.Should().Be("Billing.PaymentPending");
    }

    [Fact]
    public async Task Checkout_InactivePlanAndUnconfiguredMethod()
    {
        using var paymob = new FakePaymobApi();
        var settings = FakePaymobApi.Settings.ToDictionary(pair => pair.Key, pair => pair.Value);
        settings.Remove("Paymob:IntegrationIds:Kiosk");
        await using var factory = fixture.CreateFactory(settings: settings, paymob: paymob);
        using var client = factory.CreateClient();
        var plan = await PaidPlanAsync(client, "10.00");
        var inactive = await PaidPlanAsync(client, "10.00", isActive: false);
        var session = await AuthApi.RegisterAsync(client);

        var kiosk = await ProductApi.PostAsync(client, Checkout, new { clientRequestId = Guid.CreateVersion7(), planId = plan, method = "Kiosk" }, session.AccessToken);
        var off = await ProductApi.PostAsync(client, Checkout, new { clientRequestId = Guid.CreateVersion7(), planId = inactive, method = "Card" }, session.AccessToken);

        kiosk.Status.Should().Be(HttpStatusCode.BadRequest);
        kiosk.Code.Should().Be("Billing.MethodUnavailable");
        off.Status.Should().Be(HttpStatusCode.BadRequest);
        off.Code.Should().Be("Plan.Inactive");
    }

    [Fact]
    public async Task Checkout_PaymentsDisabled_MethodUnavailableButReadsWork()
    {
        await using var factory = fixture.CreateFactory();
        using var client = factory.CreateClient();
        var plan = await PaidPlanAsync(client, "10.00");
        var session = await AuthApi.RegisterAsync(client);

        var checkout = await ProductApi.PostAsync(client, Checkout, new { clientRequestId = Guid.CreateVersion7(), planId = plan, method = "Card" }, session.AccessToken);
        var plans = await ProductApi.GetAsync(client, Plans, session.AccessToken);
        var payments = await ProductApi.GetAsync(client, Payments, session.AccessToken);

        checkout.Status.Should().Be(HttpStatusCode.BadRequest);
        checkout.Code.Should().Be("Billing.MethodUnavailable");
        plans.Status.Should().Be(HttpStatusCode.OK);
        payments.Status.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Checkout_ProviderFails_502NothingStoredAndRetryWithSameIdWorks()
    {
        using var paymob = new FakePaymobApi { IntentionFailure = HttpStatusCode.InternalServerError };
        await using var factory = Factory(paymob);
        using var client = factory.CreateClient();
        var plan = await PaidPlanAsync(client, "10.00");
        var session = await AuthApi.RegisterAsync(client);
        var body = new { clientRequestId = Guid.CreateVersion7(), planId = plan, method = "Card" };

        var failed = await ProductApi.PostAsync(client, Checkout, body, session.AccessToken);
        var stored = await fixture.ScalarAsync<long>("SELECT count(*) FROM payments WHERE shop_id = @p0", session.ShopId);
        paymob.IntentionFailure = null;
        var retried = await ProductApi.PostAsync(client, Checkout, body, session.AccessToken);

        failed.Status.Should().Be(HttpStatusCode.BadGateway);
        failed.Code.Should().Be("Billing.ProviderUnavailable");
        failed.ContentType.Should().Be("application/problem+json");
        stored.Should().Be(0, "nothing is persisted as pending when the provider fails");
        retried.Status.Should().Be(HttpStatusCode.Created, retried.Raw);
    }

    [Fact]
    public async Task Payments_AnotherShop_404AndCancelRules()
    {
        using var paymob = new FakePaymobApi();
        await using var factory = Factory(paymob);
        using var client = factory.CreateClient();
        var plan = await PaidPlanAsync(client, "10.00");
        var owner = await AuthApi.RegisterAsync(client);
        var stranger = await AuthApi.RegisterAsync(client);
        var created = await ProductApi.PostAsync(client, Checkout, new { clientRequestId = Guid.CreateVersion7(), planId = plan, method = "Card" }, owner.AccessToken);
        var id = created.Body!["id"]!.GetValue<Guid>();

        var foreignRead = await ProductApi.GetAsync(client, $"{Payments}/{id}", stranger.AccessToken);
        var foreignCancel = await ProductApi.PostAsync(client, $"{Payments}/{id}/cancel", new { }, stranger.AccessToken);
        var staffCancel = await ProductApi.PostAsync(client, $"{Payments}/{id}/cancel", new { }, TestJwt.Mint(owner.UserId, owner.ShopId, "Staff"));
        var cancelled = await ProductApi.PostAsync(client, $"{Payments}/{id}/cancel", new { }, owner.AccessToken);
        var again = await ProductApi.PostAsync(client, $"{Payments}/{id}/cancel", new { }, owner.AccessToken);
        var pageTooBig = await ProductApi.GetAsync(client, $"{Payments}?pageSize=101", owner.AccessToken);

        foreignRead.Status.Should().Be(HttpStatusCode.NotFound);
        foreignRead.Code.Should().Be("Payment.NotFound");
        foreignCancel.Status.Should().Be(HttpStatusCode.NotFound);
        foreignCancel.Code.Should().Be("Payment.NotFound");
        staffCancel.Status.Should().Be(HttpStatusCode.Forbidden);
        cancelled.Status.Should().Be(HttpStatusCode.OK, cancelled.Raw);
        cancelled.Body!["status"]!.GetValue<string>().Should().Be("Expired");
        again.Status.Should().Be(HttpStatusCode.Conflict);
        again.Code.Should().Be("Billing.NotCancelable");
        pageTooBig.Status.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Callback_SignedSuccess_PaysSwitchesThePlanAndARepeatChangesNothing()
    {
        using var paymob = new FakePaymobApi();
        await using var factory = Factory(paymob);
        using var client = factory.CreateClient();
        var plan = await PaidPlanAsync(client, "149.75");
        var session = await AuthApi.RegisterAsync(client);
        var created = await ProductApi.PostAsync(client, Checkout, new { clientRequestId = Guid.CreateVersion7(), planId = plan, method = "Card" }, session.AccessToken);
        var id = created.Body!["id"]!.GetValue<Guid>();
        var (body, hmac) = FakePaymobApi.Callback(paymob.OrderOf(id), 14975);

        var first = await PostCallbackAsync(client, body, hmac);
        var repeat = await PostCallbackAsync(client, body, hmac);
        var payment = await ProductApi.GetAsync(client, $"{Payments}/{id}", session.AccessToken);
        var subscription = await ProductApi.GetAsync(client, Subscription, session.AccessToken);

        first.StatusCode.Should().Be(HttpStatusCode.OK);
        repeat.StatusCode.Should().Be(HttpStatusCode.OK);
        payment.Body!["status"]!.GetValue<string>().Should().Be("Paid");
        payment.Body!["paidAt"].Should().NotBeNull();
        subscription.Body!["plan"]!["id"]!.GetValue<Guid>().Should().Be(plan);
        subscription.Body!["endsOn"].Should().NotBeNull();
        subscription.Body!["pendingPayment"].Should().BeNull();
        (await fixture.ScalarAsync<long>("SELECT count(*) FROM shop_subscriptions WHERE shop_id = @p0", session.ShopId))
            .Should().Be(2, "the free subscription ended and one paid month started, once");
    }

    [Fact]
    public async Task Callback_InvalidSignature_401WithoutBodyAndNothingChanges()
    {
        using var paymob = new FakePaymobApi();
        await using var factory = Factory(paymob);
        using var client = factory.CreateClient();
        var plan = await PaidPlanAsync(client, "10.00");
        var session = await AuthApi.RegisterAsync(client);
        var created = await ProductApi.PostAsync(client, Checkout, new { clientRequestId = Guid.CreateVersion7(), planId = plan, method = "Card" }, session.AccessToken);
        var id = created.Body!["id"]!.GetValue<Guid>();
        var (body, _) = FakePaymobApi.Callback(paymob.OrderOf(id), 1000);

        var response = await PostCallbackAsync(client, body, new string('a', 128));
        var unknownOrder = FakePaymobApi.Callback("123456789012", 1000);
        var unknown = await PostCallbackAsync(client, unknownOrder.Body, unknownOrder.Hmac);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await response.Content.ReadAsStringAsync()).Should().BeEmpty();
        (await fixture.ScalarAsync<short>("SELECT status FROM payments WHERE id = @p0", id)).Should().Be((short)1);
        unknown.StatusCode.Should().Be(HttpStatusCode.OK, "an unknown order is acknowledged and logged by id only");
    }

    [Fact]
    public async Task Callback_AmountMismatch_200ButNotPaid()
    {
        using var paymob = new FakePaymobApi();
        await using var factory = Factory(paymob);
        using var client = factory.CreateClient();
        var plan = await PaidPlanAsync(client, "10.00");
        var session = await AuthApi.RegisterAsync(client);
        var created = await ProductApi.PostAsync(client, Checkout, new { clientRequestId = Guid.CreateVersion7(), planId = plan, method = "Card" }, session.AccessToken);
        var id = created.Body!["id"]!.GetValue<Guid>();
        var (body, hmac) = FakePaymobApi.Callback(paymob.OrderOf(id), 100);

        var response = await PostCallbackAsync(client, body, hmac);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await fixture.ScalarAsync<short>("SELECT status FROM payments WHERE id = @p0", id)).Should().Be((short)1, "10.00 EGP is 1000 piastres, not 100");
    }

    [Fact]
    public async Task AdminPayments_ListFilterAndSummary()
    {
        using var paymob = new FakePaymobApi();
        await using var factory = Factory(paymob);
        using var client = factory.CreateClient();
        var admin = await AdminApi.LoginAsync(client);
        var plan = await PaidPlanAsync(client, "75.25");
        var session = await AuthApi.RegisterAsync(client);
        var created = await ProductApi.PostAsync(client, Checkout, new { clientRequestId = Guid.CreateVersion7(), planId = plan, method = "Card" }, session.AccessToken);
        var id = created.Body!["id"]!.GetValue<Guid>();
        var (body, hmac) = FakePaymobApi.Callback(paymob.OrderOf(id), 7525, transactionId: 555000111);
        await PostCallbackAsync(client, body, hmac);

        var list = await AdminApi.GetAsync(client, $"{AdminPayments}?shopId={session.ShopId}&status=Paid", admin.AccessToken);
        var none = await AdminApi.GetAsync(client, $"{AdminPayments}?shopId={session.ShopId}&status=Pending", admin.AccessToken);
        var summary = await AdminApi.GetAsync(client, $"{AdminPayments}/summary", admin.AccessToken);
        var tooLong = await AdminApi.GetAsync(client, $"{AdminPayments}/summary?from=2026-01-01&to=2026-06-01", admin.AccessToken);
        var shopToken = await AdminApi.GetAsync(client, AdminPayments, session.AccessToken);

        list.Status.Should().Be(HttpStatusCode.OK, list.Raw);
        var row = list.Body!["items"]!.AsArray().Single()!;
        row["id"]!.GetValue<Guid>().Should().Be(id);
        row["shopId"]!.GetValue<Guid>().Should().Be(session.ShopId);
        row["provider"]!.GetValue<string>().Should().Be("Paymob");
        row["providerTransactionId"]!.GetValue<string>().Should().Be("555000111");
        row["amount"]!.GetValue<string>().Should().Be("75.25");
        none.Body!["totalCount"]!.GetValue<int>().Should().Be(0);
        summary.Status.Should().Be(HttpStatusCode.OK, summary.Raw);
        summary.Body!["paidCount"]!.GetValue<int>().Should().BeGreaterThanOrEqualTo(1);
        summary.Body!["byMethod"]!.AsArray().Should().Contain(entry => entry!["method"]!.GetValue<string>() == "Card");
        tooLong.Status.Should().Be(HttpStatusCode.BadRequest);
        shopToken.Status.Should().Be(HttpStatusCode.Forbidden);
    }

    private static readonly Guid DefaultPlanId = Guid.Parse("0192f0a0-0000-7000-8000-000000000001");

    private StocklyApiFactory Factory(FakePaymobApi paymob) => fixture.CreateFactory(settings: FakePaymobApi.Settings, paymob: paymob);

    private static async Task<Guid> PaidPlanAsync(HttpClient client, string price, bool isActive = true)
    {
        var admin = await AdminApi.LoginAsync(client);
        var code = $"pay-{Guid.NewGuid():N}"[..20];
        var created = await AdminApi.PostAsync(
            client, AdminApi.Plans, new { code, name = "Pro", dailyScanQuota = 100, maxMembers = 5, monthlyPrice = price, isActive }, admin.AccessToken);
        created.Status.Should().Be(HttpStatusCode.Created, created.Raw);
        return created.Body!["id"]!.GetValue<Guid>();
    }

    private static async Task<HttpResponseMessage> PostCallbackAsync(HttpClient client, string body, string hmac)
    {
        using var content = new StringContent(body);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        return await client.PostAsync(new Uri($"{Callback}?hmac={hmac}", UriKind.Relative), content);
    }
}
