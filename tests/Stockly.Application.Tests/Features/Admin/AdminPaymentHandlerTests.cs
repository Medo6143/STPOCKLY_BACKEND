using NSubstitute;
using Stockly.Application.Abstractions.Admin;
using Stockly.Application.Abstractions.Payments;
using Stockly.Application.Features.Admin;
using Stockly.Application.Features.Admin.ApplyPaymentCallback;
using Stockly.Application.Features.Admin.DowngradeLapsedSubscriptions;
using Stockly.Application.Features.Admin.GetPayments;
using Stockly.Application.Features.Admin.GetPaymentSummary;
using Stockly.Application.Features.Admin.ReconcilePayments;
using Stockly.Application.Tests.Support;
using Stockly.Contracts.Admin;
using Stockly.Contracts.Common;
using Stockly.Domain.Common;
using Stockly.Domain.Payments;
using Stockly.Domain.Shops;
using Stockly.Domain.Users;
using static Stockly.Application.Tests.Support.AdminTestContext;
using ContractStatus = Stockly.Contracts.Billing.PaymentStatus;

namespace Stockly.Application.Tests.Features.Admin;

/// <summary>
/// The provider callback, the reconcile and downgrade jobs (fake clock) and AD21–AD22 (ADR-0015 §4, §5). The concurrent
/// double callback runs on PostgreSQL: Infrastructure <c>PaymentPersistenceTests</c>.
/// </summary>
public sealed class AdminPaymentHandlerTests
{
    private static readonly Guid ShopId = Guid.Parse("0192f0a0-0000-7000-8000-0000000000a1");
    private static readonly Guid ProPlan = Guid.Parse("0192f0a0-0000-7000-8000-0000000000b1");
    private static readonly ProviderCallback Callback = new("{}", new Dictionary<string, string> { ["hmac"] = "x" });

    private readonly AdminTestContext _context = new();
    private readonly IAdminPaymentRepository _payments = Substitute.For<IAdminPaymentRepository>();
    private readonly IAdminPaymentQueries _queries = Substitute.For<IAdminPaymentQueries>();
    private readonly IPaymentGateway _gateway = Substitute.For<IPaymentGateway>();
    private readonly List<ShopSubscription> _started = [];
    private readonly PaymentSettlement _settlement;

    public AdminPaymentHandlerTests()
    {
        _gateway.Provider.Returns(PaymentProvider.Paymob);
        _context.Shops.When(shops => shops.AddSubscriptionAsync(Arg.Any<ShopSubscription>(), Arg.Any<CancellationToken>()))
            .Do(call => _started.Add(call.Arg<ShopSubscription>()));
        _settlement = new PaymentSettlement(_payments, _context.Shops, _context.UnitOfWork, _context.Clock);
    }

    [Fact]
    public async Task Callback_InvalidSignature_ReturnsInvalidCallbackAndTouchesNothing()
    {
        _gateway.ReadCallback(Callback).Returns(Result.Failure<ProviderTransaction>(PaymentErrors.InvalidCallback));

        var result = await HandleCallback();

        result.Error.Should().Be(PaymentErrors.InvalidCallback);
        result.Error.Type.Should().Be(ErrorType.Unauthorized);
        await _payments.DidNotReceiveWithAnyArgs().FindByProviderOrderForUpdateAsync(default, default!, default);
    }

    [Fact]
    public async Task Callback_UnknownOrder_SucceedsWithNothingChanged()
    {
        Signed(Transaction("999", 199.50m));

        var result = await HandleCallback();

        result.Value.Should().Be(new PaymentCallbackResult(null, SettlementOutcome.UnknownOrder));
        _context.UnitOfWork.SaveCount.Should().Be(0);
    }

    [Fact]
    public async Task Callback_SuccessSamePlan_PaidAndExtendsTheSubscriptionFromItsEnd()
    {
        var payment = Pending();
        var current = Subscription(ProPlan, endsOn: Today.AddDays(10));
        Arrange(payment, current);
        Signed(Transaction(payment.ProviderOrderId, 199.50m));

        var result = await HandleCallback();

        result.Value.Should().Be(new PaymentCallbackResult(payment.Id, SettlementOutcome.Paid));
        payment.Status.Should().Be(PaymentStatus.Paid);
        payment.PaidAt.Should().Be(Now);
        current.EndsOn.Should().Be(Today.AddDays(10).AddMonths(1));
        _started.Should().BeEmpty();
        _context.UnitOfWork.SaveCount.Should().Be(1);
    }

    [Fact]
    public async Task Callback_SuccessOtherPlan_EndsTheCurrentAndStartsThePaidMonthWithTheOverride()
    {
        var payment = Pending();
        var current = NewSubscription(ShopId, quotaOverride: 55);
        Arrange(payment, current);
        Signed(Transaction(payment.ProviderOrderId, 199.50m));

        var result = await HandleCallback();

        result.Value.Outcome.Should().Be(SettlementOutcome.Paid);
        current.Status.Should().Be(SubscriptionStatus.Ended);
        current.EndsOn.Should().Be(Today);
        var next = _started.Single();
        next.PlanId.Should().Be(ProPlan);
        next.StartsOn.Should().Be(Today);
        next.EndsOn.Should().Be(Today.AddMonths(1));
        next.DailyScanQuotaOverride.Should().Be(55);
    }

    [Fact]
    public async Task Callback_Repeat_SecondTimeChangesNothing()
    {
        var payment = Pending();
        var current = Subscription(ProPlan, endsOn: Today.AddDays(10));
        Arrange(payment, current);
        Signed(Transaction(payment.ProviderOrderId, 199.50m));

        await HandleCallback();
        var repeat = await HandleCallback();

        repeat.Value.Outcome.Should().Be(SettlementOutcome.Unchanged);
        current.EndsOn.Should().Be(Today.AddDays(10).AddMonths(1), "one payment buys one month");
    }

    [Theory]
    [InlineData("265715202", "199.49", "EGP")]
    [InlineData("265715202", "199.50", "USD")]
    [InlineData("other-order", "199.50", "EGP")]
    public async Task Callback_AmountCurrencyOrOrderMismatch_NeverPaid(string orderId, string amount, string currency)
    {
        var payment = Pending();
        Arrange(payment, Subscription(ProPlan, endsOn: Today));
        _payments.FindByProviderOrderForUpdateAsync(PaymentProvider.Paymob, orderId, Arg.Any<CancellationToken>()).Returns(payment);
        Signed(Transaction(orderId, decimal.Parse(amount, System.Globalization.CultureInfo.InvariantCulture)) with { Currency = currency });

        var result = await HandleCallback();

        result.Value.Outcome.Should().Be(SettlementOutcome.Mismatch);
        payment.Status.Should().Be(PaymentStatus.Pending);
        _context.UnitOfWork.SaveCount.Should().Be(0);
    }

    [Fact]
    public async Task Callback_Declined_FailsThePaymentWithTheReason()
    {
        var payment = Pending();
        Arrange(payment, null);
        Signed(Transaction(payment.ProviderOrderId, 199.50m, ProviderTransactionOutcome.Failed));

        var result = await HandleCallback();

        result.Value.Outcome.Should().Be(SettlementOutcome.Failed);
        payment.Status.Should().Be(PaymentStatus.Failed);
        payment.FailureReason.Should().Be("Declined");
    }

    [Fact]
    public async Task Callback_StillPending_ChangesNothing()
    {
        var payment = Pending();
        Arrange(payment, null);
        Signed(Transaction(payment.ProviderOrderId, 199.50m, ProviderTransactionOutcome.Pending));

        (await HandleCallback()).Value.Outcome.Should().Be(SettlementOutcome.StillPending);
        payment.Status.Should().Be(PaymentStatus.Pending);
    }

    [Fact]
    public async Task Callback_LateSuccessAfterCancel_BecomesPaid()
    {
        var payment = Pending();
        payment.Cancel();
        Arrange(payment, null);
        Signed(Transaction(payment.ProviderOrderId, 199.50m));

        (await HandleCallback()).Value.Outcome.Should().Be(SettlementOutcome.Paid);

        payment.Status.Should().Be(PaymentStatus.Paid);
        _started.Single().PlanId.Should().Be(ProPlan, "a shop without an active subscription gets the paid month");
    }

    [Fact]
    public async Task Reconcile_PaidAtProvider_PaysAndExpiresTheRest()
    {
        var paid = Pending("order-paid");
        var unpaid = Pending("order-unpaid");
        var stillWaiting = Pending("order-waiting");
        _payments.ListExpiredPendingAsync(Now, AdminLimits.ReconcileBatchSize, Arg.Any<CancellationToken>())
            .Returns([Expired(paid), Expired(unpaid), Expired(stillWaiting)]);
        foreach (var payment in new[] { paid, unpaid, stillWaiting })
        {
            _payments.FindForUpdateAsync(payment.Id, Arg.Any<CancellationToken>()).Returns(payment);
        }

        _payments.FindActiveSubscriptionForUpdateAsync(ShopId, Arg.Any<CancellationToken>()).Returns(Subscription(ProPlan, Today));
        _gateway.FindLatestTransactionAsync("order-paid", Arg.Any<CancellationToken>())
            .Returns(Result.Success<ProviderTransaction?>(Transaction("order-paid", 199.50m)));
        _gateway.FindLatestTransactionAsync("order-unpaid", Arg.Any<CancellationToken>()).Returns(Result.Success<ProviderTransaction?>(null));
        _gateway.FindLatestTransactionAsync("order-waiting", Arg.Any<CancellationToken>())
            .Returns(Result.Success<ProviderTransaction?>(Transaction("order-waiting", 199.50m, ProviderTransactionOutcome.Pending)));

        var result = await Reconcile();

        result.Value.Should().Be(new ReconcileSummary(3, 1, 2, 0, 0));
        paid.Status.Should().Be(PaymentStatus.Paid);
        unpaid.Status.Should().Be(PaymentStatus.Expired);
        stillWaiting.Status.Should().Be(PaymentStatus.Expired);
    }

    [Fact]
    public async Task Reconcile_ProviderError_SkipsAndLeavesThePaymentPending()
    {
        var payment = Pending();
        _payments.ListExpiredPendingAsync(Now, AdminLimits.ReconcileBatchSize, Arg.Any<CancellationToken>()).Returns([Expired(payment)]);
        _gateway.FindLatestTransactionAsync(payment.ProviderOrderId, Arg.Any<CancellationToken>())
            .Returns(Result.Failure<ProviderTransaction?>(PaymentErrors.ProviderUnavailable));

        var result = await Reconcile();

        result.Value.Should().Be(new ReconcileSummary(1, 0, 0, 0, 1));
        payment.Status.Should().Be(PaymentStatus.Pending);
        _context.UnitOfWork.CommitCount.Should().Be(0);
    }

    [Fact]
    public async Task Reconcile_OnePaymentThrows_TheRestOfTheBatchStillRuns()
    {
        // The batch is ordered oldest first: a payment that always throws must not stop every later run.
        var broken = Pending("order-broken");
        var paid = Pending("order-paid");
        _payments.ListExpiredPendingAsync(Now, AdminLimits.ReconcileBatchSize, Arg.Any<CancellationToken>())
            .Returns([Expired(broken), Expired(paid)]);
        _payments.FindForUpdateAsync(paid.Id, Arg.Any<CancellationToken>()).Returns(paid);
        _payments.FindActiveSubscriptionForUpdateAsync(ShopId, Arg.Any<CancellationToken>()).Returns(Subscription(ProPlan, Today));
        _gateway.FindLatestTransactionAsync("order-broken", Arg.Any<CancellationToken>())
            .Returns<Task<Result<ProviderTransaction?>>>(_ => throw new InvalidOperationException("unexpected answer"));
        _gateway.FindLatestTransactionAsync("order-paid", Arg.Any<CancellationToken>())
            .Returns(Result.Success<ProviderTransaction?>(Transaction("order-paid", 199.50m)));

        var result = await Reconcile();

        result.Value.Should().Be(new ReconcileSummary(2, 1, 0, 0, 1));
        broken.Status.Should().Be(PaymentStatus.Pending);
        paid.Status.Should().Be(PaymentStatus.Paid);
    }

    [Fact]
    public async Task Reconcile_SettledMeanwhile_ChangesNothing()
    {
        var payment = Pending();
        payment.MarkPaid("tx-callback", Now);
        _payments.ListExpiredPendingAsync(Now, AdminLimits.ReconcileBatchSize, Arg.Any<CancellationToken>()).Returns([Expired(payment)]);
        _payments.FindForUpdateAsync(payment.Id, Arg.Any<CancellationToken>()).Returns(payment);
        _gateway.FindLatestTransactionAsync(payment.ProviderOrderId, Arg.Any<CancellationToken>())
            .Returns(Result.Success<ProviderTransaction?>(null));

        var result = await Reconcile();

        result.Value.Should().Be(new ReconcileSummary(1, 0, 0, 0, 0));
        payment.Status.Should().Be(PaymentStatus.Paid);
    }

    [Fact]
    public async Task Downgrade_LapsedPaidSubscription_MovesToTheDefaultPlanFromTodayKeepingTheOverride()
    {
        var lapsed = ShopSubscription.Start(ShopId, ProPlan, Today.AddMonths(-1), Today.AddDays(-1), 70, Now.AddMonths(-1)).Value;
        _payments.ListShopsWithLapsedSubscriptionAsync(Today, DefaultPlan.Id, AdminLimits.DowngradeBatchSize, Arg.Any<CancellationToken>())
            .Returns([ShopId]);
        _payments.FindActiveSubscriptionForUpdateAsync(ShopId, Arg.Any<CancellationToken>()).Returns(lapsed);

        var result = await Downgrade();

        result.Value.Should().Be(1);
        lapsed.Status.Should().Be(SubscriptionStatus.Ended);
        lapsed.EndsOn.Should().Be(Today.AddDays(-1));
        var free = _started.Single();
        free.PlanId.Should().Be(DefaultPlan.Id);
        free.StartsOn.Should().Be(Today);
        free.EndsOn.Should().BeNull();
        free.DailyScanQuotaOverride.Should().Be(70);
        _context.Audited.Should().BeEmpty("a system action writes no admin audit entry");
    }

    [Fact]
    public async Task Downgrade_ExtendedMeanwhile_LeavesTheShopAlone()
    {
        var extended = Subscription(ProPlan, endsOn: Today.AddMonths(1));
        _payments.ListShopsWithLapsedSubscriptionAsync(Today, DefaultPlan.Id, AdminLimits.DowngradeBatchSize, Arg.Any<CancellationToken>())
            .Returns([ShopId]);
        _payments.FindActiveSubscriptionForUpdateAsync(ShopId, Arg.Any<CancellationToken>()).Returns(extended);

        var result = await Downgrade();

        result.Value.Should().Be(0);
        extended.Status.Should().Be(SubscriptionStatus.Active);
        _started.Should().BeEmpty();
    }

    [Fact]
    public async Task GetPayments_PassesFiltersInTheDomainStatus()
    {
        var page = new PagedResponse<AdminPayment>([], 1, 20, 0);
        _queries.ListAsync(ShopId, PaymentStatus.Paid, 1, 20, Arg.Any<PreferredLanguage>(), Arg.Any<CancellationToken>()).Returns(page);

        var result = await new GetPaymentsQueryHandler(_queries)
            .Handle(new GetPaymentsQuery(ShopId, ContractStatus.Paid, 1, 20), CancellationToken.None);

        result.Value.Should().BeSameAs(page);
    }

    [Fact]
    public async Task GetPaymentsValidator_UnknownStatusOrLargePage_IsInvalid()
    {
        var validator = new GetPaymentsQueryValidator();

        (await validator.ValidateAsync(new GetPaymentsQuery(null, null, 1, 20))).IsValid.Should().BeTrue();
        (await validator.ValidateAsync(new GetPaymentsQuery(null, (ContractStatus)42, 1, 20))).IsValid.Should().BeFalse();
        (await validator.ValidateAsync(new GetPaymentsQuery(null, null, 1, 101))).IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task GetSummary_DefaultRange_IsTheLast30CairoDays()
    {
        var summary = new PaymentSummary(0, "0.00", []);
        _queries.SummaryAsync(Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(summary);

        var result = await new GetPaymentSummaryQueryHandler(_queries, _context.Clock)
            .Handle(new GetPaymentSummaryQuery(null, null), CancellationToken.None);

        result.Value.Should().BeSameAs(summary);
        await _queries.Received(1).SummaryAsync(
            new DateTimeOffset(2025, 12, 16, 22, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 1, 15, 22, 0, 0, TimeSpan.Zero), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetSummaryValidator_RangeOver92Days_IsInvalid()
    {
        var validator = new GetPaymentSummaryQueryValidator(_context.Clock);

        (await validator.ValidateAsync(new GetPaymentSummaryQuery(Today.AddDays(-92), Today))).IsValid.Should().BeTrue();
        (await validator.ValidateAsync(new GetPaymentSummaryQuery(Today.AddDays(-93), Today))).IsValid.Should().BeFalse();
        (await validator.ValidateAsync(new GetPaymentSummaryQuery(Today, Today.AddDays(-1)))).IsValid.Should().BeFalse();
    }

    private static Payment Pending(string orderId = "265715202")
    {
        var payment = Payment.Start(ShopId, ProPlan, Guid.CreateVersion7(), 199.50m, PaymentMethod.Card, PaymentProvider.Paymob, Now.AddHours(-1)).Value;
        payment.AttachCheckout(orderId, "https://checkout.test", null, Now.AddMinutes(-30));
        return payment;
    }

    private static ExpiredPayment Expired(Payment payment) => new(payment.Id, payment.ShopId, payment.Provider, payment.ProviderOrderId);

    private static ShopSubscription Subscription(Guid planId, DateOnly endsOn) =>
        ShopSubscription.Start(ShopId, planId, Today.AddMonths(-1), endsOn, null, Now.AddMonths(-1)).Value;

    private static ProviderTransaction Transaction(
        string orderId, decimal amount, ProviderTransactionOutcome outcome = ProviderTransactionOutcome.Succeeded) =>
        new("tx-" + orderId, orderId, amount, "EGP", outcome, outcome == ProviderTransactionOutcome.Failed ? "Declined" : null);

    private void Signed(ProviderTransaction transaction) => _gateway.ReadCallback(Callback).Returns(transaction);

    private void Arrange(Payment payment, ShopSubscription? current)
    {
        _payments.FindByProviderOrderForUpdateAsync(PaymentProvider.Paymob, payment.ProviderOrderId, Arg.Any<CancellationToken>()).Returns(payment);
        _payments.FindActiveSubscriptionForUpdateAsync(ShopId, Arg.Any<CancellationToken>()).Returns(current);
    }

    private Task<Result<PaymentCallbackResult>> HandleCallback() =>
        new ApplyPaymentCallbackCommandHandler(_gateway, _payments, _settlement)
            .Handle(new ApplyPaymentCallbackCommand(Callback), CancellationToken.None);

    private Task<Result<ReconcileSummary>> Reconcile() =>
        new ReconcilePaymentsCommandHandler(_payments, _gateway, _settlement, _context.UnitOfWork, _context.Clock)
            .Handle(new ReconcilePaymentsCommand(), CancellationToken.None);

    private Task<Result<int>> Downgrade() =>
        new DowngradeLapsedSubscriptionsCommandHandler(_payments, _context.Shops, _context.UnitOfWork, _context.Clock)
            .Handle(new DowngradeLapsedSubscriptionsCommand(), CancellationToken.None);
}
