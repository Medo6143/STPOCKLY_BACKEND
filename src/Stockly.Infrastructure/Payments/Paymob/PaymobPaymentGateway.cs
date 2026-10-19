using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Polly.CircuitBreaker;
using Polly.Timeout;
using Stockly.Application.Abstractions.Payments;
using Stockly.Application.Abstractions.Time;
using Stockly.Domain.Common;
using Stockly.Domain.Payments;

namespace Stockly.Infrastructure.Payments.Paymob;

/// <summary>
/// <see cref="IPaymentGateway"/> on Paymob (ADR-0015 §1–§4), over HTTP through the typed client's resilience pipeline.
/// Checkout: the Intention API (<c>POST v1/intention/</c>, amount in piastres, the method's integration id, the owner as
/// billing data, <c>special_reference</c> = our payment id); card → Unified Checkout URL; wallet → the pay request with
/// the wallet phone (Paymob's wallet page); kiosk → the pay request with the aggregator source (the bill reference).
/// Callbacks: HMAC-SHA512 of the documented fields (<see cref="PaymobHmac"/>). Reconcile: the transaction inquiry by
/// order id with an auth token. It only talks to Paymob (SOLID S). Never logs keys, bodies, signatures or phone numbers.
/// </summary>
internal sealed partial class PaymobPaymentGateway(
    HttpClient http,
    PaymobOptions options,
    IClock clock,
    ILogger<PaymobPaymentGateway> logger) : IPaymentGateway
{
    private const string TokenScheme = "Token";
    private const string TransactionType = "TRANSACTION";
    private const string WalletSubtype = "WALLET";
    private const string Aggregator = "AGGREGATOR";
    private const string HmacParameter = "hmac";
    private const string NotAvailable = "NA";
    private const int ItemNameMaxLength = 50;
    private const int NameMaxLength = 50;

    public PaymentProvider Provider => PaymentProvider.Paymob;

    public bool IsAvailable(PaymentMethod method) => options.Enabled && options.IntegrationIdFor(method) is not null;

    public async Task<Result<ProviderCheckout>> CreateCheckoutAsync(ProviderCheckoutRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!IsAvailable(request.Method))
        {
            return PaymentErrors.MethodUnavailable;
        }

        var intention = await SendAsync(BuildIntention(request), cancellationToken);
        var created = intention is null ? null : ReadIntention(intention);
        if (created is null)
        {
            LogCheckoutFailed(logger, request.PaymentId, request.Method, "intention");
            return PaymentErrors.ProviderUnavailable;
        }

        var now = clock.UtcNow;
        var cardExpiry = now.AddMinutes(options.PendingExpiryMinutes);
        switch (request.Method)
        {
            case PaymentMethod.Card:
                LogCheckoutCreated(logger, request.PaymentId, request.Method, created.OrderId);
                return new ProviderCheckout(created.OrderId, UnifiedCheckoutUrl(created.ClientSecret), null, cardExpiry);

            case PaymentMethod.Wallet:
                var wallet = await PayAsync(created.PaymentKey, request.WalletPhone ?? string.Empty, WalletSubtype, cancellationToken);
                var walletUrl = AsUrl(wallet?["redirect_url"]) ?? AsUrl(wallet?["iframe_redirection_url"]);
                if (walletUrl is null)
                {
                    LogCheckoutFailed(logger, request.PaymentId, request.Method, "wallet pay");
                    return PaymentErrors.ProviderUnavailable;
                }

                LogCheckoutCreated(logger, request.PaymentId, request.Method, created.OrderId);
                return new ProviderCheckout(created.OrderId, walletUrl, null, cardExpiry);

            default:
                var kiosk = await PayAsync(created.PaymentKey, Aggregator, Aggregator, cancellationToken);
                var reference = PaymobHmac.AsText(kiosk?["data"]?["bill_reference"]) ?? PaymobHmac.AsText(kiosk?["bill_reference"]);
                if (string.IsNullOrWhiteSpace(reference))
                {
                    LogCheckoutFailed(logger, request.PaymentId, request.Method, "kiosk pay");
                    return PaymentErrors.ProviderUnavailable;
                }

                LogCheckoutCreated(logger, request.PaymentId, request.Method, created.OrderId);
                return new ProviderCheckout(created.OrderId, null, reference, now.AddHours(options.KioskExpiryHours));
        }
    }

    public Result<ProviderTransaction> ReadCallback(ProviderCallback callback)
    {
        ArgumentNullException.ThrowIfNull(callback);

        if (!options.Enabled || string.IsNullOrWhiteSpace(options.HmacSecret))
        {
            LogCallbackRejected(logger, "payments disabled");
            return PaymentErrors.InvalidCallback;
        }

        if (Parse(callback.Body) is not JsonObject body
            || !string.Equals(PaymobHmac.AsText(body["type"]), TransactionType, StringComparison.OrdinalIgnoreCase)
            || body["obj"] is not JsonObject transaction)
        {
            LogCallbackRejected(logger, "not a transaction callback");
            return PaymentErrors.InvalidCallback;
        }

        callback.Query.TryGetValue(HmacParameter, out var signature);
        if (!PaymobHmac.Verify(transaction, options.HmacSecret, signature))
        {
            LogCallbackRejected(logger, "signature");
            return PaymentErrors.InvalidCallback;
        }

        var read = ReadTransaction(transaction);
        if (read is null)
        {
            LogCallbackRejected(logger, "incomplete transaction");
            return PaymentErrors.InvalidCallback;
        }

        return read;
    }

    public async Task<Result<ProviderTransaction?>> FindLatestTransactionAsync(string providerOrderId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerOrderId);

        if (!options.Enabled)
        {
            return PaymentErrors.ProviderUnavailable;
        }

        var token = PaymobHmac.AsText((await SendAsync(Json(PaymobPaths.AuthToken, new { api_key = options.ApiKey }), cancellationToken))?["token"]);
        if (string.IsNullOrWhiteSpace(token))
        {
            LogInquiryFailed(logger, providerOrderId, "auth");
            return PaymentErrors.ProviderUnavailable;
        }

        var answer = await SendRawAsync(
            Json(PaymobPaths.Inquiry, new { auth_token = token, order_id = providerOrderId }), notFoundIsEmpty: true, cancellationToken);
        if (answer.Failed)
        {
            LogInquiryFailed(logger, providerOrderId, "inquiry");
            return PaymentErrors.ProviderUnavailable;
        }

        return answer.Body is JsonObject transaction ? ReadTransaction(transaction) : null;
    }

    // Intention API request body (developers.paymob.com, "Create Intention").
    private HttpRequestMessage BuildIntention(ProviderCheckoutRequest request)
    {
        var piastres = Piastres.FromPounds(request.Amount);
        var expiresIn = request.Method == PaymentMethod.Kiosk
            ? TimeSpan.FromHours(options.KioskExpiryHours)
            : TimeSpan.FromMinutes(options.PendingExpiryMinutes);
        var (firstName, lastName) = SplitName(request.Payer.FullName);
        var body = new JsonObject
        {
            ["amount"] = piastres,
            ["currency"] = request.Currency,
            ["payment_methods"] = new JsonArray(options.IntegrationIdFor(request.Method)!.Value),
            ["items"] = new JsonArray(new JsonObject
            {
                ["name"] = Cut(request.PlanName, ItemNameMaxLength),
                ["amount"] = piastres,
                ["description"] = "Stockly plan, one month",
                ["quantity"] = 1,
            }),
            ["billing_data"] = new JsonObject
            {
                ["first_name"] = firstName,
                ["last_name"] = lastName,
                ["email"] = request.Payer.Email,
                ["phone_number"] = request.WalletPhone ?? request.Payer.Phone ?? NotAvailable,
            },
            ["special_reference"] = request.PaymentId.ToString("D"),
            ["expiration"] = (long)expiresIn.TotalSeconds,
        };
        if (options.NotificationUrl is { } notification)
        {
            body["notification_url"] = notification.ToString();
        }

        if (options.RedirectionUrl is { } redirection && request.Method != PaymentMethod.Kiosk)
        {
            body["redirection_url"] = redirection.ToString();
        }

        var message = new HttpRequestMessage(HttpMethod.Post, PaymobPaths.Intention) { Content = JsonContent.Create(body) };
        message.Headers.Authorization = new AuthenticationHeaderValue(TokenScheme, options.SecretKey);
        return message;
    }

    private Task<JsonNode?> PayAsync(string paymentKey, string identifier, string subtype, CancellationToken cancellationToken) =>
        SendAsync(Json(PaymobPaths.Pay, new { source = new { identifier, subtype }, payment_token = paymentKey }), cancellationToken);

    private string UnifiedCheckoutUrl(string clientSecret) =>
        new Uri(options.CheckoutBaseUrl!, string.Create(
            CultureInfo.InvariantCulture,
            $"?publicKey={Uri.EscapeDataString(options.PublicKey!)}&clientSecret={Uri.EscapeDataString(clientSecret)}")).ToString();

    // Any non-success status, transport error, timeout or unreadable body is "the provider failed" (null).
    private async Task<JsonNode?> SendAsync(HttpRequestMessage message, CancellationToken cancellationToken)
    {
        var answer = await SendRawAsync(message, notFoundIsEmpty: false, cancellationToken);
        return answer.Failed ? null : answer.Body;
    }

    // notFoundIsEmpty: a 404 is "nothing there" (the inquiry of an order without transactions), not a failure.
    private async Task<Answer> SendRawAsync(HttpRequestMessage message, bool notFoundIsEmpty, CancellationToken cancellationToken)
    {
        using (message)
        {
            var path = message.RequestUri?.OriginalString ?? string.Empty;
            try
            {
                using var response = await http.SendAsync(message, cancellationToken);
                if (notFoundIsEmpty && response.StatusCode == HttpStatusCode.NotFound)
                {
                    return new Answer(false, null);
                }

                if (!response.IsSuccessStatusCode)
                {
                    LogStatus(logger, path, (int)response.StatusCode);
                    return Answer.Failure;
                }

                var text = await response.Content.ReadAsStringAsync(cancellationToken);
                return Parse(text) is { } node ? new Answer(false, node) : Answer.Failure;
            }
            catch (Exception exception) when (exception is HttpRequestException or TimeoutRejectedException or BrokenCircuitException
                || (exception is OperationCanceledException && !cancellationToken.IsCancellationRequested))
            {
                LogTransport(logger, path, exception.GetType().Name);
                return Answer.Failure;
            }
        }
    }

    private static HttpRequestMessage Json(string path, object body) =>
        new(HttpMethod.Post, path) { Content = JsonContent.Create(body) };

    private static Intention? ReadIntention(JsonNode node)
    {
        var key = node["payment_keys"] is JsonArray { Count: > 0 } keys ? keys[0] : null;
        var orderId = PaymobHmac.AsText(node["intention_order_id"]) ?? PaymobHmac.AsText(key?["order_id"]);
        var clientSecret = PaymobHmac.AsText(node["client_secret"]);
        var paymentKey = PaymobHmac.AsText(key?["key"]);
        return string.IsNullOrWhiteSpace(orderId) || string.IsNullOrWhiteSpace(clientSecret)
            ? null
            : new Intention(orderId, clientSecret, paymentKey ?? string.Empty);
    }

    private static ProviderTransaction? ReadTransaction(JsonObject transaction)
    {
        var id = PaymobHmac.AsText(transaction["id"]);
        var orderId = PaymobHmac.AsText(transaction["order"]?["id"]) ?? PaymobHmac.AsText(transaction["order"]);
        var cents = PaymobHmac.AsLong(transaction["amount_cents"]);
        var currency = PaymobHmac.AsText(transaction["currency"]);
        if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(orderId) || cents is null || string.IsNullOrWhiteSpace(currency))
        {
            return null;
        }

        var success = PaymobHmac.AsBool(transaction["success"]);
        var pending = PaymobHmac.AsBool(transaction["pending"]);
        var reversed = PaymobHmac.AsBool(transaction["is_voided"]) || PaymobHmac.AsBool(transaction["is_refunded"]);
        var outcome = success && !pending && !reversed ? ProviderTransactionOutcome.Succeeded
            : pending && !reversed ? ProviderTransactionOutcome.Pending
            : ProviderTransactionOutcome.Failed;
        var reason = outcome == ProviderTransactionOutcome.Failed ? PaymobHmac.AsText(transaction["data"]?["message"]) : null;
        return new ProviderTransaction(id, orderId, Piastres.ToPounds(cents.Value), currency, outcome, reason);
    }

    private static JsonNode? Parse(string text)
    {
        try
        {
            return string.IsNullOrWhiteSpace(text) ? null : JsonNode.Parse(text);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? AsUrl(JsonNode? node) =>
        PaymobHmac.AsText(node) is { } text && Uri.TryCreate(text, UriKind.Absolute, out var url) && url.Scheme == Uri.UriSchemeHttps
            ? url.ToString()
            : null;

    private static (string First, string Last) SplitName(string fullName)
    {
        var parts = fullName.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Length switch
        {
            0 => (NotAvailable, NotAvailable),
            1 => (Cut(parts[0], NameMaxLength), NotAvailable),
            _ => (Cut(parts[0], NameMaxLength), Cut(parts[1], NameMaxLength)),
        };
    }

    private static string Cut(string value, int maxLength) => value.Length <= maxLength ? value : value[..maxLength];

    [LoggerMessage(EventId = 110, Level = LogLevel.Information, Message = "Paymob checkout for payment {PaymentId} ({Method}) created order {OrderId}")]
    private static partial void LogCheckoutCreated(ILogger logger, Guid paymentId, PaymentMethod method, string orderId);

    [LoggerMessage(EventId = 111, Level = LogLevel.Warning, Message = "Paymob checkout for payment {PaymentId} ({Method}) failed at step {Step}")]
    private static partial void LogCheckoutFailed(ILogger logger, Guid paymentId, PaymentMethod method, string step);

    [LoggerMessage(EventId = 112, Level = LogLevel.Warning, Message = "Paymob callback rejected: {Reason}")]
    private static partial void LogCallbackRejected(ILogger logger, string reason);

    [LoggerMessage(EventId = 113, Level = LogLevel.Warning, Message = "Paymob inquiry for order {OrderId} failed at step {Step}")]
    private static partial void LogInquiryFailed(ILogger logger, string orderId, string step);

    [LoggerMessage(EventId = 114, Level = LogLevel.Warning, Message = "Paymob {Path} answered HTTP {Status}")]
    private static partial void LogStatus(ILogger logger, string path, int status);

    [LoggerMessage(EventId = 115, Level = LogLevel.Warning, Message = "Paymob {Path} failed: {ExceptionType}")]
    private static partial void LogTransport(ILogger logger, string path, string exceptionType);

    private sealed record Intention(string OrderId, string ClientSecret, string PaymentKey);

    private sealed record Answer(bool Failed, JsonNode? Body)
    {
        public static readonly Answer Failure = new(true, null);
    }
}

/// <summary>The Paymob API paths, relative to <see cref="PaymobOptions.BaseUrl"/> (developers.paymob.com).</summary>
public static class PaymobPaths
{
    /// <summary>Create Intention.</summary>
    public const string Intention = "v1/intention/";

    /// <summary>Pay request with a payment key (wallet, kiosk).</summary>
    public const string Pay = "api/acceptance/payments/pay";

    /// <summary>Authentication request (auth token from the API key).</summary>
    public const string AuthToken = "api/auth/tokens";

    /// <summary>Transaction inquiry by order id.</summary>
    public const string Inquiry = "api/ecommerce/orders/transaction_inquiry";
}
