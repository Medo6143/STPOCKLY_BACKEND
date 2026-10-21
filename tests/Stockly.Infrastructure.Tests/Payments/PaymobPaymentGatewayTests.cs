using System.Net;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Stockly.Application.Abstractions.Payments;
using Stockly.Application.Abstractions.Time;
using Stockly.Domain.Payments;
using Stockly.Infrastructure.Payments.Paymob;
using Stockly.Infrastructure.Tests.Support;
using static Stockly.Infrastructure.Tests.Payments.FakePaymob;

namespace Stockly.Infrastructure.Tests.Payments;

/// <summary>
/// <c>PaymobPaymentGateway</c> through the production wiring (<c>AddPayments</c>: options, typed client, resilience)
/// against <see cref="FakePaymob"/> (ADR-0015 §9). Never calls Paymob.
/// </summary>
public sealed class PaymobPaymentGatewayTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 10, 18, 10, 0, 0, TimeSpan.Zero);
    private static readonly Guid PaymentId = Guid.Parse("0192f0a0-0000-7000-8000-0000000000c1");

    private static readonly string[] TokenInAnArray = ["auth-token-1"];
    private static readonly int[] KeyAsANumber = [1];
    private static readonly string[] NotAnObject = ["not", "an", "object"];

    private readonly FakePaymob _paymob = new();
    private readonly ListLoggerProvider _logs = new();
    private ServiceProvider? _provider;

    public void Dispose()
    {
        _provider?.Dispose();
        _paymob.Dispose();
        _logs.Dispose();
    }

    [Fact]
    public async Task Checkout_Card_SendsTheDocumentedIntentionAndReturnsTheUnifiedCheckoutUrl()
    {
        _paymob.Answer(PaymobPaths.Intention, Intention(orderId: 265715202, clientSecret: "egy_csk_test_123"));

        var result = await Gateway().CreateCheckoutAsync(Request(PaymentMethod.Card, amount: 199.50m), CancellationToken.None);

        result.Value.Should().Be(new ProviderCheckout(
            "265715202",
            "https://eg.checkout.paymob.test/?publicKey=egy_pk_test_only_public&clientSecret=egy_csk_test_123",
            null,
            Now.AddMinutes(30)));
        var request = _paymob.Requests.Single();
        request.Authorization.Should().Be("Token " + SecretKey);
        var body = request.Body!;
        body["amount"]!.GetValue<long>().Should().Be(19950, "piastres from the decimal amount");
        body["currency"]!.GetValue<string>().Should().Be("EGP");
        body["payment_methods"]!.AsArray().Select(id => id!.GetValue<int>()).Should().Equal(CardIntegration);
        body["items"]![0]!["amount"]!.GetValue<long>().Should().Be(19950);
        body["items"]![0]!["quantity"]!.GetValue<int>().Should().Be(1);
        body["items"]![0]!["name"]!.GetValue<string>().Should().Be("Pro");
        body["billing_data"]!["first_name"]!.GetValue<string>().Should().Be("Mona");
        body["billing_data"]!["last_name"]!.GetValue<string>().Should().Be("Adel Hassan");
        body["billing_data"]!["email"]!.GetValue<string>().Should().Be("mona@example.com");
        body["billing_data"]!["phone_number"]!.GetValue<string>().Should().Be("01001234567");
        body["special_reference"]!.GetValue<string>().Should().Be(PaymentId.ToString());
        body["notification_url"]!.GetValue<string>().Should().Be("https://api.stockly.test/api/payments/paymob/callback");
        body["redirection_url"]!.GetValue<string>().Should().Be("https://stockly.test/paid");
        body["expiration"]!.GetValue<long>().Should().Be(1800);
    }

    [Fact]
    public async Task Checkout_Wallet_PaysWithTheWalletPhoneAndReturnsTheWalletPage()
    {
        _paymob.Answer(PaymobPaths.Intention, Intention());
        _paymob.Answer(PaymobPaths.Pay, Json(HttpStatusCode.OK, new
        {
            pending = true,
            redirect_url = "https://accept.paymob.test/wallet/otp?token=abc",
            iframe_redirection_url = "https://accept.paymob.test/iframe",
        }));

        var result = await Gateway().CreateCheckoutAsync(Request(PaymentMethod.Wallet, walletPhone: "01012345678"), CancellationToken.None);

        result.Value.CheckoutUrl.Should().Be("https://accept.paymob.test/wallet/otp?token=abc");
        result.Value.ReferenceNumber.Should().BeNull();
        var intention = _paymob.Requests[0].Body!;
        intention["payment_methods"]![0]!.GetValue<int>().Should().Be(WalletIntegration);
        intention["billing_data"]!["phone_number"]!.GetValue<string>().Should().Be("01012345678");
        var pay = _paymob.Requests[1].Body!;
        pay["source"]!["identifier"]!.GetValue<string>().Should().Be("01012345678");
        pay["source"]!["subtype"]!.GetValue<string>().Should().Be("WALLET");
        pay["payment_token"]!.GetValue<string>().Should().Be("payment-key-1");
        _logs.Entries.Select(entry => entry.Message).Should().NotContain(message => message.Contains("01012345678", StringComparison.Ordinal), "phone numbers are never logged");
    }

    [Fact]
    public async Task Checkout_WalletWithoutRedirect_ReturnsProviderUnavailable()
    {
        _paymob.Answer(PaymobPaths.Intention, Intention());
        _paymob.Answer(PaymobPaths.Pay, Json(HttpStatusCode.OK, new { pending = false, success = false }));

        var result = await Gateway().CreateCheckoutAsync(Request(PaymentMethod.Wallet, walletPhone: "01012345678"), CancellationToken.None);

        result.Error.Should().Be(PaymentErrors.ProviderUnavailable);
    }

    [Fact]
    public async Task Checkout_Kiosk_ReturnsTheBillReferenceAndTheConfiguredExpiry()
    {
        _paymob.Answer(PaymobPaths.Intention, Intention(orderId: 42));
        _paymob.Answer(PaymobPaths.Pay, Json(HttpStatusCode.OK, new { pending = true, data = new { bill_reference = 7788991 } }));

        var result = await Gateway().CreateCheckoutAsync(Request(PaymentMethod.Kiosk), CancellationToken.None);

        result.Value.Should().Be(new ProviderCheckout("42", null, "7788991", Now.AddHours(48)));
        var intention = _paymob.Requests[0].Body!;
        intention["payment_methods"]![0]!.GetValue<int>().Should().Be(KioskIntegration);
        intention.AsObject().ContainsKey("redirection_url").Should().BeFalse("redirection is for card and wallet only");
        intention["expiration"]!.GetValue<long>().Should().Be(48 * 3600);
        var pay = _paymob.Requests[1].Body!;
        pay["source"]!["identifier"]!.GetValue<string>().Should().Be("AGGREGATOR");
        pay["source"]!["subtype"]!.GetValue<string>().Should().Be("AGGREGATOR");
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task Checkout_ProviderErrorStatus_ReturnsProviderUnavailableWithoutRetry(HttpStatusCode status)
    {
        _paymob.Answer(PaymobPaths.Intention, Status(status));

        var result = await Gateway().CreateCheckoutAsync(Request(PaymentMethod.Card), CancellationToken.None);

        result.Error.Should().Be(PaymentErrors.ProviderUnavailable);
        _paymob.CountFor(PaymobPaths.Intention).Should().Be(1, "a POST is never repeated: it could create a second checkout");
        _logs.Entries.Select(entry => entry.Message).Should().NotContain(message => message.Contains("secret detail", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Checkout_Timeout_ReturnsProviderUnavailable()
    {
        _paymob.Answer(PaymobPaths.Intention, Hang());

        var result = await Gateway().CreateCheckoutAsync(Request(PaymentMethod.Card), CancellationToken.None);

        result.Error.Should().Be(PaymentErrors.ProviderUnavailable);
    }

    [Fact]
    public async Task Checkout_UnreadableAnswer_ReturnsProviderUnavailable()
    {
        _paymob.Answer(PaymobPaths.Intention, _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Created)
        {
            Content = new StringContent("<html>not json</html>"),
        }));

        (await Gateway().CreateCheckoutAsync(Request(PaymentMethod.Card), CancellationToken.None)).Error
            .Should().Be(PaymentErrors.ProviderUnavailable);
    }

    [Fact]
    public async Task Checkout_MethodWithoutIntegrationId_IsUnavailableAndCallsNothing()
    {
        var settings = FakePaymob.Settings();
        settings.Remove("Paymob:IntegrationIds:Kiosk");
        var gateway = Gateway(settings);

        gateway.IsAvailable(PaymentMethod.Kiosk).Should().BeFalse();
        gateway.IsAvailable(PaymentMethod.Card).Should().BeTrue();
        (await gateway.CreateCheckoutAsync(Request(PaymentMethod.Kiosk), CancellationToken.None)).Error
            .Should().Be(PaymentErrors.MethodUnavailable);
        _paymob.Requests.Should().BeEmpty();
    }

    [Fact]
    public void Disabled_WithoutSecretKey_EveryMethodUnavailableAndNoOtherSecretRequired()
    {
        var gateway = Gateway(new Dictionary<string, string?> { ["Paymob:IntegrationIds:Card"] = "1" });

        Enum.GetValues<PaymentMethod>().Should().OnlyContain(method => !gateway.IsAvailable(method));
    }

    [Fact]
    public void Options_EnabledWithoutHmacSecret_FailsAtStartupNamingTheVariable()
    {
        var settings = FakePaymob.Settings();
        settings.Remove("Paymob:HmacSecret");

        var act = () => Gateway(settings);

        act.Should().Throw<OptionsValidationException>().WithMessage("*Paymob__HmacSecret*")
            .Which.Message.Should().NotContain(SecretKey, "configured values are never echoed");
    }

    [Fact]
    public void ReadCallback_ValidHmac_ReturnsTheSuccessfulTransactionInPounds()
    {
        var (body, hmac) = Callback("265715202", 192036465, 19950);

        var result = Gateway().ReadCallback(new ProviderCallback(body, Query(hmac)));

        result.Value.Should().Be(new ProviderTransaction("192036465", "265715202", 199.50m, "EGP", ProviderTransactionOutcome.Succeeded, null));
    }

    [Fact]
    public void ReadCallback_UpperCaseHmac_IsAccepted()
    {
        var (body, hmac) = Callback("1", 2, 100);

        Gateway().ReadCallback(new ProviderCallback(body, Query(hmac.ToUpperInvariant()))).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void ReadCallback_TamperedAmount_ReturnsInvalidCallback()
    {
        var (body, hmac) = Callback("265715202", 192036465, 19950);
        var tampered = body.Replace("19950", "100", StringComparison.Ordinal);

        Gateway().ReadCallback(new ProviderCallback(tampered, Query(hmac))).Error.Should().Be(PaymentErrors.InvalidCallback);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("0000")]
    public void ReadCallback_MissingOrWrongHmac_ReturnsInvalidCallback(string? hmac)
    {
        var (body, _) = Callback("265715202", 192036465, 19950);
        var query = hmac is null ? new Dictionary<string, string>() : Query(hmac);

        Gateway().ReadCallback(new ProviderCallback(body, query)).Error.Should().Be(PaymentErrors.InvalidCallback);
    }

    [Fact]
    public void ReadCallback_NotATransaction_ReturnsInvalidCallback()
    {
        var body = new JsonObject { ["type"] = "TOKEN", ["obj"] = new JsonObject { ["token"] = "x" } }.ToJsonString();

        Gateway().ReadCallback(new ProviderCallback(body, Query("abc"))).Error.Should().Be(PaymentErrors.InvalidCallback);
        Gateway().ReadCallback(new ProviderCallback("not json", Query("abc"))).Error.Should().Be(PaymentErrors.InvalidCallback);
    }

    [Fact]
    public void ReadCallback_Declined_IsFailedWithTheProviderMessage()
    {
        var (body, hmac) = Callback("265715202", 192036465, 19950, success: false);

        var result = Gateway().ReadCallback(new ProviderCallback(body, Query(hmac)));

        result.Value.Outcome.Should().Be(ProviderTransactionOutcome.Failed);
        result.Value.FailureReason.Should().Be("Do not honour");
    }

    [Fact]
    public void ReadCallback_PendingKiosk_IsPending()
    {
        var (body, hmac) = Callback("265715202", 192036465, 19950, success: false, pending: true);

        Gateway().ReadCallback(new ProviderCallback(body, Query(hmac))).Value.Outcome.Should().Be(ProviderTransactionOutcome.Pending);
    }

    [Fact]
    public void ReadCallback_PaymentsDisabled_ReturnsInvalidCallback()
    {
        var (body, hmac) = Callback("1", 2, 100);

        Gateway(new Dictionary<string, string?>()).ReadCallback(new ProviderCallback(body, Query(hmac))).Error
            .Should().Be(PaymentErrors.InvalidCallback);
    }

    [Fact]
    public void Hmac_DocumentedFieldOrder()
    {
        PaymobHmac.Fields.Should().Equal(
            "amount_cents", "created_at", "currency", "error_occured", "has_parent_transaction", "id", "integration_id",
            "is_3d_secure", "is_auth", "is_capture", "is_refunded", "is_standalone_payment", "is_voided", "order.id", "owner",
            "pending", "source_data.pan", "source_data.sub_type", "source_data.type", "success");
    }

    [Fact]
    public void Hmac_IsSha512OfTheConcatenatedValues()
    {
        var transaction = Transaction("265715202", 192036465, 19950);
        const string Concatenated =
            "199502026-10-18T13:42:08.456634EGPfalsefalse1920364654101truefalsefalsefalsetruefalse265715202302852false2346MasterCardcardtrue";
        var expected = Convert.ToHexStringLower(System.Security.Cryptography.HMACSHA512.HashData(
            System.Text.Encoding.UTF8.GetBytes(HmacSecret), System.Text.Encoding.UTF8.GetBytes(Concatenated)));

        PaymobHmac.Compute(transaction, HmacSecret).Should().Be(expected);
    }

    [Fact]
    public async Task FindLatestTransaction_PaidOrder_UsesAnAuthTokenAndReturnsTheTransaction()
    {
        _paymob.Answer(PaymobPaths.AuthToken, Json(HttpStatusCode.Created, new { token = "auth-token-1", profile = new { id = 1 } }));
        _paymob.Answer(PaymobPaths.Inquiry, Json(HttpStatusCode.OK, Transaction("265715202", 477136036, 19950)));

        var result = await Gateway().FindLatestTransactionAsync("265715202", CancellationToken.None);

        result.Value.Should().Be(new ProviderTransaction("477136036", "265715202", 199.50m, "EGP", ProviderTransactionOutcome.Succeeded, null));
        _paymob.Requests[0].Body!["api_key"]!.GetValue<string>().Should().Be(ApiKey);
        _paymob.Requests[1].Body!["auth_token"]!.GetValue<string>().Should().Be("auth-token-1");
        _paymob.Requests[1].Body!["order_id"]!.GetValue<string>().Should().Be("265715202");
    }

    [Fact]
    public async Task FindLatestTransaction_NoTransaction_ReturnsNull()
    {
        _paymob.Answer(PaymobPaths.AuthToken, Json(HttpStatusCode.Created, new { token = "auth-token-1" }));
        _paymob.Answer(PaymobPaths.Inquiry, Status(HttpStatusCode.NotFound));

        var result = await Gateway().FindLatestTransactionAsync("265715202", CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeNull();
    }

    [Fact]
    public async Task FindLatestTransaction_ProviderError_ReturnsProviderUnavailable()
    {
        _paymob.Answer(PaymobPaths.AuthToken, Status(HttpStatusCode.InternalServerError));

        (await Gateway().FindLatestTransactionAsync("1", CancellationToken.None)).Error.Should().Be(PaymentErrors.ProviderUnavailable);
    }

    [Fact]
    public async Task FindLatestTransaction_OrderAsANumber_ReadsItsId()
    {
        var transaction = Transaction("265715202", 477136036, 19950);
        transaction["order"] = 265715202;
        _paymob.Answer(PaymobPaths.AuthToken, Json(HttpStatusCode.Created, new { token = "auth-token-1" }));
        _paymob.Answer(PaymobPaths.Inquiry, Json(HttpStatusCode.OK, transaction));

        var result = await Gateway().FindLatestTransactionAsync("265715202", CancellationToken.None);

        result.Value!.ProviderOrderId.Should().Be("265715202");
    }

    [Fact]
    public async Task FindLatestTransaction_DataNotAnObject_FailedWithoutAReason()
    {
        var transaction = Transaction("265715202", 477136036, 19950, success: false);
        transaction["data"] = "declined";
        _paymob.Answer(PaymobPaths.AuthToken, Json(HttpStatusCode.Created, new { token = "auth-token-1" }));
        _paymob.Answer(PaymobPaths.Inquiry, Json(HttpStatusCode.OK, transaction));

        var result = await Gateway().FindLatestTransactionAsync("265715202", CancellationToken.None);

        result.Value!.Outcome.Should().Be(ProviderTransactionOutcome.Failed);
        result.Value.FailureReason.Should().BeNull();
    }

    [Fact]
    public async Task FindLatestTransaction_AuthAnswerNotAnObject_ReturnsProviderUnavailable()
    {
        _paymob.Answer(PaymobPaths.AuthToken, Json(HttpStatusCode.Created, TokenInAnArray));

        (await Gateway().FindLatestTransactionAsync("1", CancellationToken.None)).Error.Should().Be(PaymentErrors.ProviderUnavailable);
    }

    [Fact]
    public async Task Checkout_AnswersOfAnotherShape_ReturnProviderUnavailableInsteadOfThrowing()
    {
        // Each step answers JSON of an unexpected shape (a string, a number, an array where an object is read).
        _paymob.Answer(PaymobPaths.Intention, Json(HttpStatusCode.Created, new { client_secret = "egy_csk_test_123", payment_keys = KeyAsANumber }));
        (await Gateway().CreateCheckoutAsync(Request(PaymentMethod.Card), CancellationToken.None)).Error
            .Should().Be(PaymentErrors.ProviderUnavailable);

        _paymob.Answer(PaymobPaths.Intention, Intention(orderId: 42));
        _paymob.Answer(PaymobPaths.Pay, Json(HttpStatusCode.OK, NotAnObject));
        (await Gateway().CreateCheckoutAsync(Request(PaymentMethod.Wallet, walletPhone: "01012345678"), CancellationToken.None)).Error
            .Should().Be(PaymentErrors.ProviderUnavailable);

        _paymob.Answer(PaymobPaths.Intention, Intention(orderId: 43));
        _paymob.Answer(PaymobPaths.Pay, Json(HttpStatusCode.OK, new { pending = true, data = "no reference" }));
        (await Gateway().CreateCheckoutAsync(Request(PaymentMethod.Kiosk), CancellationToken.None)).Error
            .Should().Be(PaymentErrors.ProviderUnavailable);
    }

    private static Dictionary<string, string> Query(string hmac) => new() { ["hmac"] = hmac };

    private static ProviderCheckoutRequest Request(PaymentMethod method, decimal amount = 199.50m, string? walletPhone = null) =>
        new(PaymentId, amount, "EGP", method, "Pro", new Payer("Mona Adel Hassan", "mona@example.com", "01001234567"), walletPhone);

    private IPaymentGateway Gateway(Dictionary<string, string?>? settings = null)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings ?? FakePaymob.Settings()).Build();
        var services = new ServiceCollection();
        services.AddLogging(logging => logging.AddProvider(_logs));
        services.AddSingleton<IClock>(new FakeClock(Now));
        services.AddPayments(configuration);
        services.ConfigureAll<HttpClientFactoryOptions>(options =>
            options.HttpMessageHandlerBuilderActions.Add(builder => builder.PrimaryHandler = _paymob));
        _provider?.Dispose();
        _provider = services.BuildServiceProvider(validateScopes: true);
        _ = _provider.GetRequiredService<IOptions<PaymobOptions>>().Value;
        return _provider.GetRequiredService<IPaymentGateway>();
    }
}
