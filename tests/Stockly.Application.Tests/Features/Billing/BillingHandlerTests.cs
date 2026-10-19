using NSubstitute;
using Stockly.Application.Abstractions.Identity;
using Stockly.Application.Abstractions.Payments;
using Stockly.Application.Abstractions.Persistence;
using Stockly.Application.Abstractions.Time;
using Stockly.Application.Features.Auth;
using Stockly.Application.Features.Billing.CancelPayment;
using Stockly.Application.Features.Billing.Checkout;
using Stockly.Application.Features.Billing.GetBillingPlans;
using Stockly.Application.Features.Billing.GetPayment;
using Stockly.Application.Features.Billing.GetPayments;
using Stockly.Application.Features.Billing.GetSubscription;
using Stockly.Application.Tests.Support;
using Stockly.Contracts.Billing;
using Stockly.Contracts.Common;
using Stockly.Domain.Common;
using Stockly.Domain.Payments;
using Stockly.Domain.Shops;
using Stockly.Domain.Users;
using ContractMethod = Stockly.Contracts.Billing.PaymentMethod;
using ContractStatus = Stockly.Contracts.Billing.PaymentStatus;
using DomainMethod = Stockly.Domain.Payments.PaymentMethod;
using PaymentProvider = Stockly.Domain.Payments.PaymentProvider;

namespace Stockly.Application.Tests.Features.Billing;

/// <summary>
/// B1–B6 handlers and validators (contract §12.2). Role checks (Staff → 403) are the Owner policy, covered by the Api
/// <c>BillingEndpointTests</c> with the concurrent and SQL parts; another shop's payment is invisible to the tenant-filtered
/// ports, so it comes back as <c>Payment.NotFound</c>.
/// </summary>
public sealed class BillingHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 18, 10, 0, 0, TimeSpan.Zero);
    private static readonly Guid ShopId = Guid.Parse("0192f0a0-0000-7000-8000-000000000001");
    private static readonly Guid UserId = Guid.Parse("0192f0a0-0000-7000-8000-000000000002");

    private readonly ICurrentUser _user = Substitute.For<ICurrentUser>();
    private readonly ICurrentTenant _tenant = Substitute.For<ICurrentTenant>();
    private readonly IUserAccounts _users = Substitute.For<IUserAccounts>();
    private readonly IPlanRepository _plans = Substitute.For<IPlanRepository>();
    private readonly IPaymentRepository _payments = Substitute.For<IPaymentRepository>();
    private readonly IBillingQueries _billing = Substitute.For<IBillingQueries>();
    private readonly IPaymentGateway _gateway = Substitute.For<IPaymentGateway>();
    private readonly IInvoiceRepository _invoices = Substitute.For<IInvoiceRepository>();
    private readonly IShopMembershipRepository _memberships = Substitute.For<IShopMembershipRepository>();
    private readonly IClock _clock = Substitute.For<IClock>();
    private readonly InlineUnitOfWork _unitOfWork = new();
    private readonly List<Payment> _added = [];
    private readonly Plan _pro = Plan.Create("pro", "احترافية", "Pro", 100, 5, 199.50m).Value;
    private readonly PaymentDto _dto;

    public BillingHandlerTests()
    {
        _user.UserId.Returns(UserId);
        _user.Role.Returns(ShopRole.Owner);
        _tenant.ShopId.Returns(ShopId);
        _clock.UtcNow.Returns(Now);
        _users.FindByIdAsync(UserId, Arg.Any<CancellationToken>())
            .Returns(new UserAccount(UserId, "mona@example.com", "Mona Adel", "01001234567", PreferredLanguage.Ar));
        _plans.FindAsync(_pro.Id, Arg.Any<CancellationToken>()).Returns(_pro);
        _gateway.Provider.Returns(PaymentProvider.Paymob);
        _gateway.IsAvailable(Arg.Any<DomainMethod>()).Returns(true);
        _gateway.CreateCheckoutAsync(Arg.Any<ProviderCheckoutRequest>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(new ProviderCheckout("265715202", "https://checkout.test/pay", null, Now.AddMinutes(30))));
        _payments.When(payments => payments.Add(Arg.Any<Payment>())).Do(call => _added.Add(call.Arg<Payment>()));
        _dto = Dto(ContractStatus.Pending);
        _billing.FindPaymentAsync(Arg.Any<Guid>(), Arg.Any<PreferredLanguage>(), Arg.Any<CancellationToken>()).Returns(_dto);
    }

    [Fact]
    public async Task Checkout_NewPayment_CallsTheProviderWithThePlanPriceThenStoresItPending()
    {
        var command = Checkout(ContractMethod.Card);

        var result = await HandleCheckout(command);

        result.Value.Should().Be(new CheckoutResult(_dto, Created: true));
        var payment = _added.Single();
        payment.Amount.Should().Be(199.50m, "the amount is the plan's price, never a client value");
        payment.Status.Should().Be(Stockly.Domain.Payments.PaymentStatus.Pending);
        payment.ProviderOrderId.Should().Be("265715202");
        payment.CheckoutUrl.Should().Be("https://checkout.test/pay");
        payment.ShopId.Should().Be(ShopId);
        await _gateway.Received(1).CreateCheckoutAsync(
            Arg.Is<ProviderCheckoutRequest>(request => request.PaymentId == payment.Id
                && request.Amount == 199.50m
                && request.Currency == "EGP"
                && request.Method == DomainMethod.Card
                && request.WalletPhone == null
                && request.Payer.Email == "mona@example.com"),
            Arg.Any<CancellationToken>());
        await _payments.Received(1).LockCheckoutsAsync(Arg.Any<CancellationToken>());
        _unitOfWork.CommitCount.Should().Be(1);
    }

    [Fact]
    public async Task Checkout_Wallet_PassesTheWalletPhone()
    {
        await HandleCheckout(Checkout(ContractMethod.Wallet, "01012345678"));

        await _gateway.Received(1).CreateCheckoutAsync(
            Arg.Is<ProviderCheckoutRequest>(request => request.Method == DomainMethod.Wallet && request.WalletPhone == "01012345678"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Checkout_RepeatClientRequestId_ReturnsExistingWithoutCallingTheProvider()
    {
        var command = Checkout(ContractMethod.Card);
        _billing.FindPaymentByClientRequestIdAsync(command.ClientRequestId, Arg.Any<PreferredLanguage>(), Arg.Any<CancellationToken>())
            .Returns(_dto);

        var result = await HandleCheckout(command);

        result.Value.Should().Be(new CheckoutResult(_dto, Created: false));
        await _gateway.DidNotReceiveWithAnyArgs().CreateCheckoutAsync(default!, default);
        _added.Should().BeEmpty();
    }

    [Fact]
    public async Task Checkout_ConcurrentRepeatWonTheInsert_ReturnsTheWinner()
    {
        var command = Checkout(ContractMethod.Card);
        _unitOfWork.TrySaveResult = PaymentErrors.DuplicateClientRequest;
        _billing.FindPaymentByClientRequestIdAsync(command.ClientRequestId, Arg.Any<PreferredLanguage>(), Arg.Any<CancellationToken>())
            .Returns(null, null, _dto);

        var result = await HandleCheckout(command);

        result.Value.Should().Be(new CheckoutResult(_dto, Created: false));
        _unitOfWork.CommitCount.Should().Be(0);
    }

    [Fact]
    public async Task Checkout_UnknownPlan_ReturnsPlanNotFound()
    {
        var result = await HandleCheckout(Checkout(ContractMethod.Card) with { PlanId = Guid.CreateVersion7() });

        result.Error.Should().Be(PlanErrors.NotFound);
        await _gateway.DidNotReceiveWithAnyArgs().CreateCheckoutAsync(default!, default);
    }

    [Fact]
    public async Task Checkout_InactivePlan_ReturnsPlanInactive()
    {
        _pro.Update("احترافية", "Pro", 100, 5, 199.50m, isActive: false);

        (await HandleCheckout(Checkout(ContractMethod.Card))).Error.Should().Be(PlanErrors.Inactive);
    }

    [Fact]
    public async Task Checkout_FreePlan_ReturnsFreePlan()
    {
        var free = Plan.Create("free", "مجانية", "Free", 10, 1, 0m).Value;
        _plans.FindAsync(free.Id, Arg.Any<CancellationToken>()).Returns(free);

        var result = await HandleCheckout(Checkout(ContractMethod.Card) with { PlanId = free.Id });

        result.Error.Should().Be(PaymentErrors.FreePlan);
    }

    [Fact]
    public async Task Checkout_MethodNotConfigured_ReturnsMethodUnavailable()
    {
        _gateway.IsAvailable(DomainMethod.Kiosk).Returns(false);

        var result = await HandleCheckout(Checkout(ContractMethod.Kiosk));

        result.Error.Should().Be(PaymentErrors.MethodUnavailable);
        result.Error.Code.Should().Be("Billing.MethodUnavailable");
    }

    [Fact]
    public async Task Checkout_OpenPendingPayment_ReturnsPaymentPendingBeforeCallingTheProvider()
    {
        _payments.HasOpenPaymentAsync(Now, Arg.Any<CancellationToken>()).Returns(true);

        var result = await HandleCheckout(Checkout(ContractMethod.Card));

        result.Error.Should().Be(PaymentErrors.PaymentPending);
        await _gateway.DidNotReceiveWithAnyArgs().CreateCheckoutAsync(default!, default);
    }

    [Fact]
    public async Task Checkout_PendingPaymentCommittedMeanwhile_ReturnsPaymentPendingAndStoresNothing()
    {
        _payments.HasOpenPaymentAsync(Now, Arg.Any<CancellationToken>()).Returns(false, true);

        var result = await HandleCheckout(Checkout(ContractMethod.Card));

        result.Error.Should().Be(PaymentErrors.PaymentPending);
        _added.Should().BeEmpty();
    }

    [Fact]
    public async Task Checkout_ProviderFails_ReturnsProviderUnavailableAndStoresNothing()
    {
        _gateway.CreateCheckoutAsync(Arg.Any<ProviderCheckoutRequest>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure<ProviderCheckout>(PaymentErrors.ProviderUnavailable));

        var result = await HandleCheckout(Checkout(ContractMethod.Card));

        result.Error.Should().Be(PaymentErrors.ProviderUnavailable);
        result.Error.Type.Should().Be(ErrorType.BadGateway);
        _added.Should().BeEmpty();
        _unitOfWork.SaveCount.Should().Be(0);
    }

    [Fact]
    public async Task Checkout_NoTenant_ReturnsUnauthenticated()
    {
        _tenant.ShopId.Returns((Guid?)null);

        (await HandleCheckout(Checkout(ContractMethod.Card))).Error.Should().Be(AuthErrors.Unauthenticated);
    }

    [Fact]
    public async Task CheckoutValidator_RequestShape()
    {
        var validator = new CheckoutCommandValidator();

        (await validator.ValidateAsync(Checkout(ContractMethod.Card))).IsValid.Should().BeTrue();
        (await validator.ValidateAsync(Checkout(ContractMethod.Kiosk, "not a phone"))).IsValid.Should().BeTrue("ignored for kiosk");
        (await validator.ValidateAsync(Checkout(ContractMethod.Wallet, "01012345678"))).IsValid.Should().BeTrue();
        (await validator.ValidateAsync(Checkout(ContractMethod.Wallet))).IsValid.Should().BeFalse();
        (await validator.ValidateAsync(Checkout(ContractMethod.Wallet, "01312345678"))).IsValid.Should().BeFalse();
        (await validator.ValidateAsync(Checkout(ContractMethod.Wallet, "0101234567"))).IsValid.Should().BeFalse();
        (await validator.ValidateAsync(Checkout(ContractMethod.Card) with { ClientRequestId = Guid.Empty })).IsValid.Should().BeFalse();
        (await validator.ValidateAsync(Checkout(ContractMethod.Card) with { PlanId = Guid.Empty })).IsValid.Should().BeFalse();
        (await validator.ValidateAsync(Checkout((ContractMethod)99))).IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task GetPlans_ReturnsTheActivePlans()
    {
        var plans = new[] { new BillingPlan(_pro.Id, "pro", "Pro", "199.50", 100, 5, false, false) };
        _billing.ListActivePlansAsync(Arg.Any<PreferredLanguage>(), Arg.Any<CancellationToken>()).Returns(plans);

        var result = await new GetBillingPlansQueryHandler(_billing).Handle(new GetBillingPlansQuery(), CancellationToken.None);

        result.Value.Should().BeSameAs(plans);
    }

    [Fact]
    public async Task GetSubscription_CombinesPlanUsageMembersAndPendingPayment()
    {
        var plan = new BillingPlan(DefaultPlan.Id, "standard", "Standard", "0.00", 30, 3, true, true);
        _billing.FindActiveSubscriptionAsync(Arg.Any<PreferredLanguage>(), Arg.Any<CancellationToken>())
            .Returns(new ActiveSubscription(plan, new DateOnly(2026, 10, 1), null, 40));
        _billing.FindNewestPendingPaymentAsync(Arg.Any<PreferredLanguage>(), Arg.Any<CancellationToken>()).Returns(_dto);
        _invoices.CountCreatedSinceAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(7);
        _memberships.CountActiveMembersAsync(UserId, ShopId, Arg.Any<CancellationToken>()).Returns(2);

        var result = await GetSubscription();

        result.Value.Should().Be(new SubscriptionSummary(plan, new DateOnly(2026, 10, 1), null, 40, 7, 2, _dto));
        await _invoices.Received(1).CountCreatedSinceAsync(new DateTimeOffset(2026, 10, 17, 21, 0, 0, TimeSpan.Zero), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetSubscription_NoUser_ReturnsUnauthenticated()
    {
        _user.UserId.Returns((Guid?)null);

        (await GetSubscription()).Error.Should().Be(AuthErrors.Unauthenticated);
    }

    [Fact]
    public async Task GetPayment_Found_ReturnsIt()
    {
        var result = await new GetPaymentQueryHandler(_billing).Handle(new GetPaymentQuery(_dto.Id), CancellationToken.None);

        result.Value.Should().BeSameAs(_dto);
    }

    [Fact]
    public async Task GetPayment_MissingOrAnotherShops_ReturnsNotFound()
    {
        _billing.FindPaymentAsync(Arg.Any<Guid>(), Arg.Any<PreferredLanguage>(), Arg.Any<CancellationToken>()).Returns((PaymentDto?)null);

        var result = await new GetPaymentQueryHandler(_billing).Handle(new GetPaymentQuery(Guid.CreateVersion7()), CancellationToken.None);

        result.Error.Should().Be(PaymentErrors.NotFound);
    }

    [Fact]
    public async Task GetPayments_ReturnsThePage()
    {
        var page = new PagedResponse<PaymentDto>([_dto], 2, 10, 11);
        _billing.ListPaymentsAsync(2, 10, Arg.Any<PreferredLanguage>(), Arg.Any<CancellationToken>()).Returns(page);

        var result = await new GetPaymentsQueryHandler(_billing).Handle(new GetPaymentsQuery(2, 10), CancellationToken.None);

        result.Value.Should().BeSameAs(page);
    }

    [Fact]
    public async Task GetPaymentsValidator_PageSizeOver100_IsInvalid()
    {
        var validator = new GetPaymentsQueryValidator();

        (await validator.ValidateAsync(new GetPaymentsQuery(1, 100))).IsValid.Should().BeTrue();
        (await validator.ValidateAsync(new GetPaymentsQuery(1, 101))).IsValid.Should().BeFalse();
        (await validator.ValidateAsync(new GetPaymentsQuery(0, 20))).IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task Cancel_Pending_BecomesExpiredAndIsSaved()
    {
        var payment = PendingPayment();
        _payments.FindForUpdateAsync(payment.Id, Arg.Any<CancellationToken>()).Returns(payment);

        var result = await Cancel(payment.Id);

        result.Value.Should().BeSameAs(_dto);
        payment.Status.Should().Be(Stockly.Domain.Payments.PaymentStatus.Expired);
        _unitOfWork.SaveCount.Should().Be(1);
    }

    [Fact]
    public async Task Cancel_Paid_ReturnsNotCancelable()
    {
        var payment = PendingPayment();
        payment.MarkPaid("tx-1", Now);
        _payments.FindForUpdateAsync(payment.Id, Arg.Any<CancellationToken>()).Returns(payment);

        var result = await Cancel(payment.Id);

        result.Error.Should().Be(PaymentErrors.NotCancelable);
        _unitOfWork.SaveCount.Should().Be(0);
    }

    [Fact]
    public async Task Cancel_MissingOrAnotherShops_ReturnsNotFound()
    {
        (await Cancel(Guid.CreateVersion7())).Error.Should().Be(PaymentErrors.NotFound);
    }

    private CheckoutCommand Checkout(ContractMethod method, string? walletPhone = null) =>
        new(Guid.CreateVersion7(), _pro.Id, method, walletPhone);

    private static PaymentDto Dto(ContractStatus status) =>
        new(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), "Pro", "199.50", "EGP", ContractMethod.Card, status,
            "https://checkout.test/pay", null, Now.AddMinutes(30).UtcDateTime, null, Now.UtcDateTime, null);

    private Payment PendingPayment()
    {
        var payment = Payment.Start(ShopId, _pro.Id, Guid.CreateVersion7(), 199.50m, DomainMethod.Card, PaymentProvider.Paymob, Now).Value;
        payment.AttachCheckout("1", null, null, Now.AddMinutes(30));
        return payment;
    }

    private Task<Result<CheckoutResult>> HandleCheckout(CheckoutCommand command) =>
        new CheckoutCommandHandler(_user, _tenant, _users, _plans, _payments, _billing, _gateway, _unitOfWork, _clock)
            .Handle(command, CancellationToken.None);

    private Task<Result<SubscriptionSummary>> GetSubscription() =>
        new GetSubscriptionQueryHandler(_user, _tenant, _billing, _invoices, _memberships, _clock)
            .Handle(new GetSubscriptionQuery(), CancellationToken.None);

    private Task<Result<PaymentDto>> Cancel(Guid paymentId) =>
        new CancelPaymentCommandHandler(_payments, _billing, _unitOfWork).Handle(new CancelPaymentCommand(paymentId), CancellationToken.None);
}
