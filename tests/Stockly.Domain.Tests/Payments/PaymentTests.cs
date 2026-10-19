using Stockly.Domain.Common;
using Stockly.Domain.Payments;
using static Stockly.Domain.Tests.TestData;

namespace Stockly.Domain.Tests.Payments;

/// <summary>The payment state machine (ADR-0015 §4, contract §12): Pending → Paid | Failed | Expired; Paid is final.</summary>
public sealed class PaymentTests
{
    private static readonly Guid PlanId = Guid.Parse("0192f0a0-0000-7000-8000-0000000000aa");
    private static readonly DateTimeOffset Expiry = Now.AddMinutes(30);

    [Fact]
    public void Start_ValidInput_IsPendingTenantPaymentInEgp()
    {
        var clientRequestId = Guid.CreateVersion7();

        var payment = Start(clientRequestId: clientRequestId);

        payment.Should().BeAssignableTo<ITenantEntity>();
        payment.ShopId.Should().Be(ShopId);
        payment.PlanId.Should().Be(PlanId);
        payment.ClientRequestId.Should().Be(clientRequestId);
        payment.Amount.Should().Be(199.50m);
        payment.Currency.Should().Be("EGP");
        payment.Method.Should().Be(PaymentMethod.Card);
        payment.Provider.Should().Be(PaymentProvider.Paymob);
        payment.Status.Should().Be(PaymentStatus.Pending);
        payment.ProviderOrderId.Should().Be("265715202");
        payment.CheckoutUrl.Should().Be("https://checkout.test/pay");
        payment.ExpiresAt.Should().Be(Expiry);
        payment.CreatedAt.Should().Be(Now);
        payment.PaidAt.Should().BeNull();
        payment.ProviderTransactionId.Should().BeNull();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Start_NotPositiveAmount_ReturnsFreePlan(int amount)
    {
        Payment.Start(ShopId, PlanId, Guid.CreateVersion7(), amount, PaymentMethod.Card, PaymentProvider.Paymob, Now)
            .Error.Should().Be(PaymentErrors.FreePlan);
    }

    [Fact]
    public void Start_ThreeDecimalAmount_ReturnsTooManyDecimals()
    {
        Payment.Start(ShopId, PlanId, Guid.CreateVersion7(), 1.005m, PaymentMethod.Card, PaymentProvider.Paymob, Now)
            .Error.Code.Should().Be("Precision.TooManyDecimals");
    }

    [Fact]
    public void Start_MissingIds_ReturnGuards()
    {
        Payment.Start(Guid.Empty, PlanId, Guid.CreateVersion7(), 1m, PaymentMethod.Card, PaymentProvider.Paymob, Now)
            .Error.Should().Be(PaymentErrors.ShopRequired);
        Payment.Start(ShopId, Guid.Empty, Guid.CreateVersion7(), 1m, PaymentMethod.Card, PaymentProvider.Paymob, Now)
            .Error.Should().Be(PaymentErrors.PlanRequired);
        Payment.Start(ShopId, PlanId, Guid.Empty, 1m, PaymentMethod.Card, PaymentProvider.Paymob, Now)
            .Error.Should().Be(PaymentErrors.ClientRequestIdRequired);
        Payment.Start(ShopId, PlanId, Guid.CreateVersion7(), 1m, (PaymentMethod)99, PaymentProvider.Paymob, Now)
            .Error.Should().Be(PaymentErrors.UnknownMethod);
        Payment.Start(ShopId, PlanId, Guid.CreateVersion7(), 1m, PaymentMethod.Card, PaymentProvider.Paymob, Now)
            .Value.AttachCheckout(" ", null, null, Expiry).Error.Should().Be(PaymentErrors.ProviderOrderRequired);
    }

    [Fact]
    public void AttachCheckout_Twice_ReturnsCheckoutAttachedAndKeepsTheOrder()
    {
        var payment = Start();

        payment.AttachCheckout("other", null, "123", Expiry).Error.Should().Be(PaymentErrors.CheckoutAttached);

        payment.ProviderOrderId.Should().Be("265715202");
        payment.ReferenceNumber.Should().BeNull();
    }

    [Fact]
    public void AttachCheckout_Kiosk_KeepsTheReference()
    {
        var payment = Payment.Start(ShopId, PlanId, Guid.CreateVersion7(), 10m, PaymentMethod.Kiosk, PaymentProvider.Paymob, Now).Value;

        payment.AttachCheckout("42", null, " 7788991 ", Expiry).IsSuccess.Should().BeTrue();

        payment.ReferenceNumber.Should().Be("7788991");
        payment.CheckoutUrl.Should().BeNull();
    }

    [Fact]
    public void MarkPaid_Pending_BecomesPaidWithTransactionAndTime()
    {
        var payment = Start();

        var changed = payment.MarkPaid("tx-1", Now.AddMinutes(5));

        changed.Value.Should().BeTrue();
        payment.Status.Should().Be(PaymentStatus.Paid);
        payment.ProviderTransactionId.Should().Be("tx-1");
        payment.PaidAt.Should().Be(Now.AddMinutes(5));
    }

    [Fact]
    public void MarkPaid_AlreadyPaid_IsFinalAndChangesNothing()
    {
        var payment = Start();
        payment.MarkPaid("tx-1", Now);

        var again = payment.MarkPaid("tx-2", Now.AddHours(1));

        again.Value.Should().BeFalse();
        payment.ProviderTransactionId.Should().Be("tx-1");
        payment.PaidAt.Should().Be(Now);
    }

    [Fact]
    public void MarkPaid_AfterCancel_LateSuccessBecomesPaid()
    {
        var payment = Start();
        payment.Cancel().IsSuccess.Should().BeTrue();

        payment.MarkPaid("tx-late", Now.AddHours(2)).Value.Should().BeTrue();

        payment.Status.Should().Be(PaymentStatus.Paid);
    }

    [Fact]
    public void MarkPaid_AfterFailedAttempt_BecomesPaidAndClearsTheReason()
    {
        var payment = Start();
        payment.MarkFailed("tx-1", "Declined");

        payment.MarkPaid("tx-2", Now).Value.Should().BeTrue();

        payment.Status.Should().Be(PaymentStatus.Paid);
        payment.ProviderTransactionId.Should().Be("tx-2");
        payment.FailureReason.Should().BeNull();
    }

    [Fact]
    public void MarkPaid_NoTransactionId_ReturnsTransactionRequired()
    {
        Start().MarkPaid(" ", Now).Error.Should().Be(PaymentErrors.TransactionRequired);
    }

    [Fact]
    public void MarkFailed_Pending_BecomesFailedWithReason()
    {
        var payment = Start();

        payment.MarkFailed("tx-1", "  Insufficient funds ").Value.Should().BeTrue();

        payment.Status.Should().Be(PaymentStatus.Failed);
        payment.FailureReason.Should().Be("Insufficient funds");
        payment.ProviderTransactionId.Should().Be("tx-1");
    }

    [Fact]
    public void MarkFailed_Paid_ChangesNothing()
    {
        var payment = Start();
        payment.MarkPaid("tx-1", Now);

        payment.MarkFailed("tx-2", "Declined").Value.Should().BeFalse();

        payment.Status.Should().Be(PaymentStatus.Paid);
        payment.ProviderTransactionId.Should().Be("tx-1");
    }

    [Fact]
    public void MarkFailed_LongReason_IsCut()
    {
        var payment = Start();

        payment.MarkFailed("tx-1", new string('x', 600));

        payment.FailureReason.Should().HaveLength(Payment.FailureReasonMaxLength);
    }

    [Theory]
    [InlineData(PaymentStatus.Paid)]
    [InlineData(PaymentStatus.Failed)]
    [InlineData(PaymentStatus.Expired)]
    public void Cancel_NotPending_ReturnsNotCancelable(PaymentStatus status)
    {
        var payment = InStatus(status);

        payment.Cancel().Error.Should().Be(PaymentErrors.NotCancelable);
        payment.Status.Should().Be(status);
    }

    [Fact]
    public void Cancel_Pending_BecomesExpired()
    {
        var payment = Start();

        payment.Cancel().IsSuccess.Should().BeTrue();

        payment.Status.Should().Be(PaymentStatus.Expired);
    }

    [Fact]
    public void Expire_OnlyPendingChanges()
    {
        var pending = Start();
        var paid = InStatus(PaymentStatus.Paid);

        pending.Expire().Should().BeTrue();
        paid.Expire().Should().BeFalse();

        pending.Status.Should().Be(PaymentStatus.Expired);
        paid.Status.Should().Be(PaymentStatus.Paid);
    }

    [Fact]
    public void IsOpenAt_PendingBeforeExpiry_IsOpen()
    {
        var payment = Start();

        payment.IsOpenAt(Expiry.AddSeconds(-1)).Should().BeTrue();
        payment.IsOpenAt(Expiry).Should().BeFalse();
        InStatus(PaymentStatus.Failed).IsOpenAt(Now).Should().BeFalse();
    }

    [Fact]
    public void Matches_SameOrderAmountAndCurrency_True()
    {
        var payment = Start();

        payment.Matches("265715202", 199.5m, "egp").Should().BeTrue();
        payment.Matches("265715202", 199.49m, "EGP").Should().BeFalse();
        payment.Matches("265715202", 199.5m, "USD").Should().BeFalse();
        payment.Matches("999", 199.5m, "EGP").Should().BeFalse();
    }

    [Fact]
    public void Enums_Values_AreExplicitAndStable()
    {
        Enum.GetValues<PaymentStatus>().Select(value => (int)value).Should().Equal(1, 2, 3, 4);
        Enum.GetNames<PaymentStatus>().Should().Equal("Pending", "Paid", "Failed", "Expired");
        Enum.GetValues<PaymentMethod>().Select(value => (int)value).Should().Equal(1, 2, 3);
        Enum.GetNames<PaymentMethod>().Should().Equal("Card", "Wallet", "Kiosk");
        Enum.GetNames<PaymentProvider>().Should().Equal("Paymob");
    }

    private static Payment Start(Guid? clientRequestId = null)
    {
        var payment = Payment.Start(
            ShopId, PlanId, clientRequestId ?? Guid.CreateVersion7(), 199.50m, PaymentMethod.Card, PaymentProvider.Paymob, Now).Value;
        payment.AttachCheckout(" 265715202 ", "https://checkout.test/pay", null, Expiry).IsSuccess.Should().BeTrue();
        return payment;
    }

    private static Payment InStatus(PaymentStatus status)
    {
        var payment = Start();
        _ = status switch
        {
            PaymentStatus.Paid => payment.MarkPaid("tx-1", Now).IsSuccess,
            PaymentStatus.Failed => payment.MarkFailed("tx-1", "Declined").IsSuccess,
            PaymentStatus.Expired => payment.Expire(),
            _ => true,
        };
        return payment;
    }
}
