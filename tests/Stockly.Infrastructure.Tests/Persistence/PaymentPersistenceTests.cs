using System.Globalization;
using MediatR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using Stockly.Application;
using Stockly.Application.Abstractions.Admin;
using Stockly.Application.Abstractions.Identity;
using Stockly.Application.Abstractions.Payments;
using Stockly.Application.Abstractions.Persistence;
using Stockly.Application.Abstractions.Time;
using Stockly.Application.Features.Admin;
using Stockly.Application.Features.Admin.ApplyPaymentCallback;
using Stockly.Domain.Payments;
using Stockly.Domain.Shops;
using Stockly.Domain.Users;
using Stockly.Infrastructure.Jobs;
using Stockly.Infrastructure.Payments.Paymob;
using Stockly.Infrastructure.Tests.Payments;
using Stockly.Infrastructure.Tests.Support;
using ContractPaymentMethod = Stockly.Contracts.Billing.PaymentMethod;

namespace Stockly.Infrastructure.Tests.Persistence;

/// <summary>
/// Payments on a real PostgreSQL through the production wiring (MediatR pipeline, transactions, Paymob gateway against
/// <see cref="FakePaymob"/>): tenant isolation, client-request idempotency, the signed callback including two concurrent
/// identical callbacks, the reconcile and downgrade jobs with a fake clock, and the admin reads (AD21, AD22). Its own
/// database: the jobs here work across every shop and the plans they add would leak into the other suites' checks.
/// </summary>
[Collection(PaymentPostgresCollectionDefinition.Name)]
public sealed class PaymentPersistenceTests(PostgresFixture fixture) : IAsyncDisposable
{
    // 2026-10-18 12:00 Cairo (UTC+3 in October 2026).
    private static readonly DateTimeOffset Now = new(2026, 10, 18, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 10, 18);

    private readonly FakePaymob _paymob = new();
    private readonly List<ServiceProvider> _providers = [];

    public async ValueTask DisposeAsync()
    {
        foreach (var provider in _providers)
        {
            await provider.DisposeAsync();
        }

        _paymob.Dispose();
    }

    [Fact]
    public async Task Billing_AnotherShopsPayment_IsInvisible()
    {
        var plan = await PlanAsync();
        var (shop, _) = await ShopOnPlanAsync(DefaultPlan.Id);
        var (other, _) = await ShopOnPlanAsync(DefaultPlan.Id);
        var payment = await PaymentAsync(shop, plan, "o-" + Guid.NewGuid().ToString("N"));

        var mine = await Billing(shop).FindPaymentAsync(payment, PreferredLanguage.En, CancellationToken.None);
        var theirs = await Billing(other).FindPaymentAsync(payment, PreferredLanguage.En, CancellationToken.None);

        mine.Should().NotBeNull();
        mine!.PlanName.Should().Be("Pro");
        mine.Amount.Should().Be("199.50");
        mine.Method.Should().Be(ContractPaymentMethod.Card);
        theirs.Should().BeNull("another shop's payment is 404 (rule 10)");
        (await Billing(other).ListPaymentsAsync(1, 20, PreferredLanguage.En, CancellationToken.None)).TotalCount.Should().Be(0);
    }

    [Fact]
    public async Task Billing_PlansSubscriptionAndPendingPayment_AreProjected()
    {
        var plan = await PlanAsync();
        var (shop, _) = await ShopOnPlanAsync(plan, endsOn: Today.AddDays(5), quotaOverride: 12);
        var older = await PaymentAsync(shop, plan, "o-" + Guid.NewGuid().ToString("N"), createdAt: Now.AddHours(-2));
        var newer = await PaymentAsync(shop, plan, "o-" + Guid.NewGuid().ToString("N"), createdAt: Now.AddHours(-1));
        var queries = Billing(shop);

        var plans = await queries.ListActivePlansAsync(PreferredLanguage.En, CancellationToken.None);
        var subscription = await queries.FindActiveSubscriptionAsync(PreferredLanguage.En, CancellationToken.None);
        var pending = await queries.FindNewestPendingPaymentAsync(PreferredLanguage.En, CancellationToken.None);
        var page = await queries.ListPaymentsAsync(1, 1, PreferredLanguage.En, CancellationToken.None);

        plans.Should().Contain(candidate => candidate.Id == plan && candidate.IsCurrent && !candidate.IsFree && candidate.MonthlyPrice == "199.50");
        plans.Should().Contain(candidate => candidate.Id == DefaultPlan.Id && candidate.IsFree && !candidate.IsCurrent);
        plans[0].IsFree.Should().BeTrue("free first, then by price");
        subscription!.Plan.Id.Should().Be(plan);
        subscription.EndsOn.Should().Be(Today.AddDays(5));
        subscription.EffectiveDailyScanQuota.Should().Be(12);
        pending!.Id.Should().Be(newer);
        page.TotalCount.Should().Be(2);
        page.Items.Single().Id.Should().Be(newer);
        older.Should().NotBe(newer);
    }

    [Fact]
    public async Task Repository_SameClientRequestIdTwice_SecondSaveIsDuplicateClientRequest()
    {
        var plan = await PlanAsync();
        var (shop, _) = await ShopOnPlanAsync(DefaultPlan.Id);
        var clientRequestId = Guid.CreateVersion7();
        await PaymentAsync(shop, plan, "o-" + Guid.NewGuid().ToString("N"), clientRequestId: clientRequestId);
        await using var scope = Provider(shop).CreateAsyncScope();
        var payment = NewPayment(shop, plan, "o-" + Guid.NewGuid().ToString("N"), clientRequestId, Now);

        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var saved = await unitOfWork.ExecuteInTransactionAsync(
            async token =>
            {
                scope.ServiceProvider.GetRequiredService<IPaymentRepository>().Add(payment);
                return await unitOfWork.TrySaveChangesAsync(token);
            },
            result => result.IsSuccess,
            CancellationToken.None);

        saved.Error.Should().Be(PaymentErrors.DuplicateClientRequest);
    }

    [Fact]
    public async Task Repository_OpenPayment_RespectsTheExpiry()
    {
        var plan = await PlanAsync();
        var (shop, _) = await ShopOnPlanAsync(DefaultPlan.Id);
        await PaymentAsync(shop, plan, "o-" + Guid.NewGuid().ToString("N"), expiresAt: Now.AddMinutes(10));
        await using var scope = Provider(shop).CreateAsyncScope();
        var payments = scope.ServiceProvider.GetRequiredService<IPaymentRepository>();

        (await payments.HasOpenPaymentAsync(Now, CancellationToken.None)).Should().BeTrue();
        (await payments.HasOpenPaymentAsync(Now.AddMinutes(10), CancellationToken.None)).Should().BeFalse();
    }

    [Fact]
    public async Task Callback_Valid_PaysAndSwitchesThePlanInOneTransaction()
    {
        var plan = await PlanAsync();
        var (shop, oldSubscription) = await ShopOnPlanAsync(DefaultPlan.Id, quotaOverride: 44);
        var orderId = Order();
        var payment = await PaymentAsync(shop, plan, orderId);
        var (body, hmac) = FakePaymob.Callback(orderId, TransactionId(), 19950);

        var result = await SendCallbackAsync(body, hmac);

        result.Value.Should().Be(new PaymentCallbackResult(payment, SettlementOutcome.Paid));
        (await fixture.ScalarAsync<short>("SELECT status FROM payments WHERE id = @p0", payment)).Should().Be((short)PaymentStatus.Paid);
        (await fixture.ScalarAsync<short>("SELECT status FROM shop_subscriptions WHERE id = @p0", oldSubscription))
            .Should().Be((short)SubscriptionStatus.Ended);
        (await fixture.ScalarAsync<Guid>("SELECT plan_id FROM shop_subscriptions WHERE shop_id = @p0 AND status = 1", shop)).Should().Be(plan);
        (await fixture.ScalarAsync<DateOnly>("SELECT ends_on FROM shop_subscriptions WHERE shop_id = @p0 AND status = 1", shop))
            .Should().Be(Today.AddMonths(1));
        (await fixture.ScalarAsync<int>("SELECT daily_scan_quota_override FROM shop_subscriptions WHERE shop_id = @p0 AND status = 1", shop))
            .Should().Be(44);
    }

    [Fact]
    public async Task Callback_InvalidHmac_ChangesNothing()
    {
        var plan = await PlanAsync();
        var (shop, _) = await ShopOnPlanAsync(DefaultPlan.Id);
        var orderId = Order();
        var payment = await PaymentAsync(shop, plan, orderId);
        var (body, _) = FakePaymob.Callback(orderId, TransactionId(), 19950);

        var result = await SendCallbackAsync(body, new string('0', 128));

        result.Error.Should().Be(PaymentErrors.InvalidCallback);
        (await fixture.ScalarAsync<short>("SELECT status FROM payments WHERE id = @p0", payment)).Should().Be((short)PaymentStatus.Pending);
    }

    [Fact]
    public async Task Callback_TwoConcurrentIdenticalCallbacks_ExtendTheSubscriptionOnce()
    {
        var plan = await PlanAsync();
        var (shop, subscription) = await ShopOnPlanAsync(plan, endsOn: Today.AddDays(3));
        var orderId = Order();
        var payment = await PaymentAsync(shop, plan, orderId);
        var (body, hmac) = FakePaymob.Callback(orderId, TransactionId(), 19950);

        var results = await Task.WhenAll(
            Enumerable.Range(0, 4).Select(_ => Task.Run(() => SendCallbackAsync(body, hmac))));

        results.Should().OnlyContain(result => result.IsSuccess);
        results.Count(result => result.Value.Outcome == SettlementOutcome.Paid).Should().Be(1);
        results.Count(result => result.Value.Outcome == SettlementOutcome.Unchanged).Should().Be(3);
        (await fixture.ScalarAsync<DateOnly>("SELECT ends_on FROM shop_subscriptions WHERE id = @p0", subscription))
            .Should().Be(Today.AddDays(3).AddMonths(1), "one payment buys one month");
        (await fixture.ScalarAsync<long>("SELECT count(*) FROM shop_subscriptions WHERE shop_id = @p0", shop)).Should().Be(1);
        (await fixture.ScalarAsync<short>("SELECT status FROM payments WHERE id = @p0", payment)).Should().Be((short)PaymentStatus.Paid);
    }

    [Fact]
    public async Task Callback_UnknownOrder_SucceedsAndChangesNothing()
    {
        var (body, hmac) = FakePaymob.Callback(Order(), TransactionId(), 19950);

        var result = await SendCallbackAsync(body, hmac);

        result.Value.Should().Be(new PaymentCallbackResult(null, SettlementOutcome.UnknownOrder));
    }

    [Fact]
    public async Task ReconcileJob_ExpiredPayments_PaidAtProviderBecomesPaidOthersExpire()
    {
        var plan = await PlanAsync();
        var (paidShop, _) = await ShopOnPlanAsync(DefaultPlan.Id);
        var (unpaidShop, _) = await ShopOnPlanAsync(DefaultPlan.Id);
        var (futureShop, _) = await ShopOnPlanAsync(DefaultPlan.Id);
        var paidOrder = Order();
        var paid = await PaymentAsync(paidShop, plan, paidOrder, expiresAt: Now.AddMinutes(-5));
        var unpaid = await PaymentAsync(unpaidShop, plan, Order(), expiresAt: Now.AddMinutes(-5));
        var notYet = await PaymentAsync(futureShop, plan, Order(), expiresAt: Now.AddMinutes(5));
        _paymob.Answer(PaymobPaths.AuthToken, FakePaymob.Json(System.Net.HttpStatusCode.Created, new { token = "t" }));
        _paymob.Answer(PaymobPaths.Inquiry, async token =>
        {
            var request = _paymob.Requests.Last(candidate => candidate.Path == PaymobPaths.Inquiry);
            var order = request.Body!["order_id"]!.GetValue<string>();
            return order == paidOrder
                ? await FakePaymob.Json(System.Net.HttpStatusCode.OK, FakePaymob.Transaction(paidOrder, TransactionId(), 19950))(token)
                : new HttpResponseMessage(System.Net.HttpStatusCode.NotFound);
        });

        await RunJobAsync<ReconcilePaymentsJob>(job => job.RunAsync(CancellationToken.None));

        (await fixture.ScalarAsync<short>("SELECT status FROM payments WHERE id = @p0", paid)).Should().Be((short)PaymentStatus.Paid);
        (await fixture.ScalarAsync<short>("SELECT status FROM payments WHERE id = @p0", unpaid)).Should().Be((short)PaymentStatus.Expired);
        (await fixture.ScalarAsync<short>("SELECT status FROM payments WHERE id = @p0", notYet)).Should().Be((short)PaymentStatus.Pending);
        (await fixture.ScalarAsync<Guid>("SELECT plan_id FROM shop_subscriptions WHERE shop_id = @p0 AND status = 1", paidShop)).Should().Be(plan);
        ReconcilePaymentsJob.Cron.Should().Be("*/15 * * * *");
    }

    [Fact]
    public async Task DowngradeJob_LapsedPaidSubscription_MovesToTheDefaultPlan()
    {
        var plan = await PlanAsync();
        var (lapsedShop, lapsed) = await ShopOnPlanAsync(plan, endsOn: Today.AddDays(-1), quotaOverride: 9);
        var (activeShop, active) = await ShopOnPlanAsync(plan, endsOn: Today);

        await RunJobAsync<DowngradeLapsedSubscriptionsJob>(job => job.RunAsync(CancellationToken.None));

        (await fixture.ScalarAsync<short>("SELECT status FROM shop_subscriptions WHERE id = @p0", lapsed)).Should().Be((short)SubscriptionStatus.Ended);
        (await fixture.ScalarAsync<DateOnly>("SELECT ends_on FROM shop_subscriptions WHERE id = @p0", lapsed)).Should().Be(Today.AddDays(-1));
        (await fixture.ScalarAsync<Guid>("SELECT plan_id FROM shop_subscriptions WHERE shop_id = @p0 AND status = 1", lapsedShop))
            .Should().Be(DefaultPlan.Id);
        (await fixture.ScalarAsync<int>("SELECT daily_scan_quota_override FROM shop_subscriptions WHERE shop_id = @p0 AND status = 1", lapsedShop))
            .Should().Be(9);
        (await fixture.ScalarAsync<short>("SELECT status FROM shop_subscriptions WHERE id = @p0", active)).Should().Be((short)SubscriptionStatus.Active);
        activeShop.Should().NotBe(lapsedShop);
        (await fixture.ScalarAsync<long>("SELECT count(*) FROM admin_audit_logs WHERE target_id = @p0", lapsedShop)).Should().Be(0);
    }

    [Fact]
    public async Task AdminQueries_ListAndSummary_SeeEveryShop()
    {
        var plan = await PlanAsync();
        var (shop, _) = await ShopOnPlanAsync(DefaultPlan.Id);
        var paid = await PaymentAsync(shop, plan, Order());
        await fixture.ExecuteAsync(
            "UPDATE payments SET status = 2, paid_at = @p1, provider_transaction_id = @p2 WHERE id = @p0", paid, Now.AddYears(5), "tx-" + paid);
        var pending = await PaymentAsync(shop, plan, Order(), createdAt: Now.AddMinutes(1));
        await using var scope = Provider(null).CreateAsyncScope();
        var queries = scope.ServiceProvider.GetRequiredService<IAdminPaymentQueries>();

        var all = await queries.ListAsync(shop, null, 1, 20, PreferredLanguage.Ar, CancellationToken.None);
        var onlyPaid = await queries.ListAsync(shop, PaymentStatus.Paid, 1, 20, PreferredLanguage.Ar, CancellationToken.None);
        var day = Now.AddYears(5);
        var summary = await queries.SummaryAsync(day.AddHours(-1), day.AddHours(1), CancellationToken.None);

        all.Items.Select(item => item.Id).Should().Equal(pending, paid);
        all.Items[1].ProviderTransactionId.Should().Be("tx-" + paid);
        all.Items[1].ShopId.Should().Be(shop);
        all.Items[1].PlanName.Should().Be("برو");
        onlyPaid.Items.Should().ContainSingle().Which.Id.Should().Be(paid);
        summary.PaidCount.Should().Be(1);
        summary.PaidAmount.Should().Be("199.50");
        summary.ByMethod.Should().ContainSingle().Which.Should().Be(new Contracts.Admin.PaymentMethodSummary(ContractPaymentMethod.Card, 1, "199.50"));
    }

    [Theory]
    [InlineData("ix_payments_created_at", "(created_at DESC)")]
    [InlineData("ix_payments_shop_id_created_at", "(shop_id, created_at DESC)")]
    [InlineData("ix_payments_status_created_at", "(status, created_at DESC)")]
    public async Task Migration_ListIndexes_AreNewestFirst(string index, string columns)
    {
        var definition = await fixture.ScalarAsync<string>("SELECT indexdef FROM pg_indexes WHERE indexname = @p0", index);

        definition.Should().EndWith(columns);
    }

    private static string Order() => Random.Shared.NextInt64(1_000_000, 9_000_000_000).ToString(CultureInfo.InvariantCulture);

    private static long TransactionId() => Random.Shared.NextInt64(1_000_000, 9_000_000_000);

    private static Payment NewPayment(
        Guid shop, Guid plan, string orderId, Guid clientRequestId, DateTimeOffset createdAt, DateTimeOffset? expiresAt = null)
    {
        var payment = Payment.Start(shop, plan, clientRequestId, 199.50m, PaymentMethod.Card, PaymentProvider.Paymob, createdAt).Value;
        payment.AttachCheckout(orderId, "https://checkout.test", null, expiresAt ?? Now.AddMinutes(30));
        return payment;
    }

    private async Task<Guid> PaymentAsync(
        Guid shop, Guid plan, string orderId, DateTimeOffset? createdAt = null, DateTimeOffset? expiresAt = null, Guid? clientRequestId = null)
    {
        var payment = NewPayment(shop, plan, orderId, clientRequestId ?? Guid.CreateVersion7(), createdAt ?? Now, expiresAt);
        await using var context = fixture.CreateContext(shop, new FakeClock(Now));
        context.Add(payment);
        await context.SaveChangesAsync();
        return payment.Id;
    }

    private async Task<Guid> PlanAsync()
    {
        var id = Guid.CreateVersion7();
        await fixture.ExecuteAsync(
            "INSERT INTO plans (id, code, name_ar, name_en, daily_scan_quota, max_members, monthly_price, is_active) " +
            "VALUES (@p0, @p1, 'برو', 'Pro', 100, 5, 199.50, true)",
            id,
            $"p-{id:N}"[..30]);
        return id;
    }

    private async Task<(Guid Shop, Guid Subscription)> ShopOnPlanAsync(Guid planId, DateOnly? endsOn = null, int? quotaOverride = null)
    {
        var shop = await fixture.CreateShopAsync();
        var subscription = Guid.CreateVersion7();
        await fixture.ExecuteAsync(
            "INSERT INTO shop_subscriptions (id, shop_id, plan_id, status, starts_on, ends_on, created_at, daily_scan_quota_override) " +
            "VALUES (@p0, @p1, @p2, 1, @p3, @p4, now(), @p5)",
            subscription,
            shop.Id,
            planId,
            Today.AddMonths(-1),
            endsOn is { } end ? end : DBNull.Value,
            quotaOverride is { } value ? value : DBNull.Value);
        return (shop.Id, subscription);
    }

    private IBillingQueries Billing(Guid shop) =>
        Provider(shop).CreateScope().ServiceProvider.GetRequiredService<IBillingQueries>();

    private async Task<Domain.Common.Result<PaymentCallbackResult>> SendCallbackAsync(string body, string hmac)
    {
        await using var scope = Provider(null).CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<ISender>().Send(
            new ApplyPaymentCallbackCommand(new ProviderCallback(body, new Dictionary<string, string> { ["hmac"] = hmac })));
    }

    private async Task RunJobAsync<TJob>(Func<TJob, Task> run)
        where TJob : notnull
    {
        await using var scope = Provider(null).CreateAsyncScope();
        await run(scope.ServiceProvider.GetRequiredService<TJob>());
    }

    // The production wiring for one tenant (null: a callback or a job), Paymob answered by the fake.
    private ServiceProvider Provider(Guid? shopId)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(FakePaymob.Settings()).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPersistence(fixture.ConnectionString);
        services.AddApplication();
        services.AddPayments(configuration);
        services.AddStocklyJobs(new ConfigurationBuilder().Build(), fixture.ConnectionString);
        services.ConfigureAll<HttpClientFactoryOptions>(options =>
            options.HttpMessageHandlerBuilderActions.Add(builder => builder.PrimaryHandler = _paymob));
        services.AddSingleton<ICurrentTenant>(new Tenant(shopId));
        services.AddSingleton<IClock>(new FakeClock(Now));
        var provider = services.BuildServiceProvider(validateScopes: true);
        _providers.Add(provider);
        return provider;
    }

    private sealed class Tenant(Guid? shopId) : ICurrentTenant
    {
        public Guid? ShopId => shopId;
    }
}

[CollectionDefinition(Name)]
public sealed class PaymentPostgresCollectionDefinition : ICollectionFixture<PostgresFixture>
{
    public const string Name = "payments-postgres";
}
