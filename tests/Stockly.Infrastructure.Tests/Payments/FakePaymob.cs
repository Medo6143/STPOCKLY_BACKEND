using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Stockly.Infrastructure.Payments.Paymob;

namespace Stockly.Infrastructure.Tests.Payments;

/// <summary>
/// A fake <see cref="HttpMessageHandler"/> in place of Paymob: no test calls the real API (ADR-0015 §9). Answers are
/// queued per path (the last one repeats); every request is recorded with its body and authorization header.
/// </summary>
internal sealed class FakePaymob : HttpMessageHandler
{
    public const string HmacSecret = "test-only-paymob-hmac-secret";
    public const string SecretKey = "sk_test_only_secret";
    public const string PublicKey = "egy_pk_test_only_public";
    public const string ApiKey = "test-only-api-key";
    public const int CardIntegration = 4101;
    public const int WalletIntegration = 4102;
    public const int KioskIntegration = 4103;

    private readonly Dictionary<string, Queue<Func<CancellationToken, Task<HttpResponseMessage>>>> _answers = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Func<CancellationToken, Task<HttpResponseMessage>>> _last = new(StringComparer.Ordinal);

    public List<Recorded> Requests { get; } = [];

    /// <summary>Configuration of a test host with every method configured.</summary>
    public static Dictionary<string, string?> Settings(int timeoutSeconds = 1) => new()
    {
        ["Paymob:SecretKey"] = SecretKey,
        ["Paymob:PublicKey"] = PublicKey,
        ["Paymob:HmacSecret"] = HmacSecret,
        ["Paymob:ApiKey"] = ApiKey,
        ["Paymob:BaseUrl"] = "https://paymob.test/",
        ["Paymob:CheckoutBaseUrl"] = "https://eg.checkout.paymob.test/",
        ["Paymob:IntegrationIds:Card"] = CardIntegration.ToString(System.Globalization.CultureInfo.InvariantCulture),
        ["Paymob:IntegrationIds:Wallet"] = WalletIntegration.ToString(System.Globalization.CultureInfo.InvariantCulture),
        ["Paymob:IntegrationIds:Kiosk"] = KioskIntegration.ToString(System.Globalization.CultureInfo.InvariantCulture),
        ["Paymob:RedirectionUrl"] = "https://stockly.test/paid",
        ["Paymob:NotificationUrl"] = "https://api.stockly.test/api/payments/paymob/callback",
        ["Paymob:TimeoutSeconds"] = timeoutSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture),
    };

    public FakePaymob Answer(string path, params Func<CancellationToken, Task<HttpResponseMessage>>[] answers)
    {
        _answers[path] = new Queue<Func<CancellationToken, Task<HttpResponseMessage>>>(answers);
        _last[path] = answers[^1];
        return this;
    }

    /// <summary>A created intention (developers.paymob.com, "Create Intention" response).</summary>
    public static Func<CancellationToken, Task<HttpResponseMessage>> Intention(long orderId = 265715202, string clientSecret = "egy_csk_test_123") =>
        Json(HttpStatusCode.Created, new
        {
            payment_keys = new[] { new { integration = CardIntegration, key = "payment-key-1", gateway_type = "MIGS", iframe_id = (int?)null, order_id = orderId } },
            intention_order_id = orderId,
            id = "pi_test_bd49bb7fb4da48cfac4ec71ab4d8c433",
            client_secret = clientSecret,
            status = "intended",
            @object = "paymentintention",
        });

    public static Func<CancellationToken, Task<HttpResponseMessage>> Json(HttpStatusCode status, object body) =>
        _ => Task.FromResult(new HttpResponseMessage(status)
        {
            Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json"),
        });

    public static Func<CancellationToken, Task<HttpResponseMessage>> Status(HttpStatusCode status) =>
        _ => Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent("{\"detail\":\"secret detail\"}") });

    public static Func<CancellationToken, Task<HttpResponseMessage>> Hang() =>
        async token =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            throw new InvalidOperationException("Unreachable.");
        };

    /// <summary>
    /// A transaction processed callback body (type TRANSACTION, the transaction in <c>obj</c>) and its HMAC computed with
    /// <see cref="HmacSecret"/>.
    /// </summary>
    public static (string Body, string Hmac) Callback(
        string orderId, long transactionId, long amountCents, bool success = true, bool pending = false, string currency = "EGP")
    {
        var transaction = Transaction(orderId, transactionId, amountCents, success, pending, currency);
        var body = new JsonObject { ["type"] = "TRANSACTION", ["obj"] = transaction.DeepClone() };
        return (body.ToJsonString(), PaymobHmac.Compute(transaction, HmacSecret));
    }

    public static JsonObject Transaction(
        string orderId, long transactionId, long amountCents, bool success = true, bool pending = false, string currency = "EGP") => new()
    {
        ["id"] = transactionId,
        ["pending"] = pending,
        ["amount_cents"] = amountCents,
        ["success"] = success,
        ["is_auth"] = false,
        ["is_capture"] = false,
        ["is_standalone_payment"] = true,
        ["is_voided"] = false,
        ["is_refunded"] = false,
        ["is_3d_secure"] = true,
        ["integration_id"] = CardIntegration,
        ["has_parent_transaction"] = false,
        ["order"] = new JsonObject { ["id"] = long.Parse(orderId, System.Globalization.CultureInfo.InvariantCulture), ["merchant_order_id"] = "ref" },
        ["created_at"] = "2026-10-18T13:42:08.456634",
        ["currency"] = currency,
        ["source_data"] = new JsonObject { ["pan"] = "2346", ["type"] = "card", ["sub_type"] = "MasterCard" },
        ["error_occured"] = false,
        ["owner"] = 302852,
        ["data"] = new JsonObject { ["message"] = success ? "Approved" : "Do not honour" },
    };

    public int CountFor(string path) => Requests.Count(request => request.Path == path);

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var path = request.RequestUri!.AbsolutePath.TrimStart('/');
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        Requests.Add(new Recorded(path, body is null ? null : JsonNode.Parse(body), request.Headers.Authorization?.ToString()));

        if (_answers.TryGetValue(path, out var queue) && queue.Count > 0)
        {
            return await queue.Dequeue()(cancellationToken);
        }

        if (_last.TryGetValue(path, out var last))
        {
            return await last(cancellationToken);
        }

        return new HttpResponseMessage(HttpStatusCode.NotFound);
    }

    public sealed record Recorded(string Path, JsonNode? Body, string? Authorization);
}
