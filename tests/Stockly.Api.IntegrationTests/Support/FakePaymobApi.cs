using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Stockly.Infrastructure.Payments.Paymob;

namespace Stockly.Api.IntegrationTests.Support;

/// <summary>
/// A fake Paymob behind an <see cref="HttpMessageHandler"/> for the Api host (ADR-0015 §9): every intention gets a new
/// order id, wallet and kiosk pay requests answer with a wallet page or a bill reference, and the inquiry answers what
/// the test arranged. Callbacks are signed with <see cref="HmacSecret"/>. Paymob itself is never called.
/// </summary>
internal sealed class FakePaymobApi : HttpMessageHandler
{
    public const string HmacSecret = "test-only-paymob-hmac-secret";

    private long _nextOrder = Random.Shared.NextInt64(1_000_000, 9_000_000_000);

    /// <summary>Answer of the intention endpoint; <c>null</c> creates intentions.</summary>
    public HttpStatusCode? IntentionFailure { get; set; }

    public ConcurrentQueue<(string Path, JsonNode? Body)> Requests { get; } = new();

    /// <summary>Settings that enable Paymob with every method, the fake's HMAC secret and fast timeouts.</summary>
    public static IReadOnlyDictionary<string, string?> Settings { get; } = new Dictionary<string, string?>
    {
        ["Paymob:SecretKey"] = "sk_test_only_secret",
        ["Paymob:PublicKey"] = "egy_pk_test_only_public",
        ["Paymob:HmacSecret"] = HmacSecret,
        ["Paymob:ApiKey"] = "test-only-api-key",
        ["Paymob:BaseUrl"] = "https://paymob.test/",
        ["Paymob:CheckoutBaseUrl"] = "https://eg.checkout.paymob.test/",
        ["Paymob:IntegrationIds:Card"] = "4101",
        ["Paymob:IntegrationIds:Wallet"] = "4102",
        ["Paymob:IntegrationIds:Kiosk"] = "4103",
        ["Paymob:TimeoutSeconds"] = "2",
    };

    public int IntentionCount => Requests.Count(request => request.Path == PaymobPaths.Intention);

    /// <summary>The order id Paymob gave the payment with this <c>special_reference</c>.</summary>
    public string OrderOf(Guid paymentId) =>
        Requests.Where(request => request.Path == PaymobPaths.Intention)
            .Select(request => request.Body!)
            .Where(body => body["special_reference"]!.GetValue<string>() == paymentId.ToString())
            .Select(body => body["fake_order"]!.GetValue<string>())
            .Single();

    /// <summary>A signed transaction processed callback for <paramref name="orderId"/>: the body and the <c>hmac</c> query value.</summary>
    public static (string Body, string Hmac) Callback(string orderId, long amountCents, bool success = true, long? transactionId = null)
    {
        var transaction = new JsonObject
        {
            ["id"] = transactionId ?? Random.Shared.NextInt64(1_000_000, 9_000_000_000),
            ["pending"] = false,
            ["amount_cents"] = amountCents,
            ["success"] = success,
            ["is_auth"] = false,
            ["is_capture"] = false,
            ["is_standalone_payment"] = true,
            ["is_voided"] = false,
            ["is_refunded"] = false,
            ["is_3d_secure"] = true,
            ["integration_id"] = 4101,
            ["has_parent_transaction"] = false,
            ["order"] = new JsonObject { ["id"] = long.Parse(orderId, CultureInfo.InvariantCulture) },
            ["created_at"] = "2026-10-18T13:42:08.456634",
            ["currency"] = "EGP",
            ["source_data"] = new JsonObject { ["pan"] = "2346", ["type"] = "card", ["sub_type"] = "MasterCard" },
            ["error_occured"] = false,
            ["owner"] = 302852,
            ["data"] = new JsonObject { ["message"] = success ? "Approved" : "Do not honour" },
        };
        var body = new JsonObject { ["type"] = "TRANSACTION", ["obj"] = transaction.DeepClone() };
        return (body.ToJsonString(), PaymobHmac.Compute(transaction, HmacSecret));
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var path = request.RequestUri!.AbsolutePath.TrimStart('/');
        var body = request.Content is null ? null : JsonNode.Parse(await request.Content.ReadAsStringAsync(cancellationToken));
        if (path == PaymobPaths.Intention)
        {
            if (IntentionFailure is { } failure)
            {
                Requests.Enqueue((path, body));
                return new HttpResponseMessage(failure);
            }

            var order = Interlocked.Increment(ref _nextOrder);
            body!["fake_order"] = order.ToString(CultureInfo.InvariantCulture);
            Requests.Enqueue((path, body));
            return Json(HttpStatusCode.Created, new
            {
                payment_keys = new[] { new { integration = 4101, key = $"key-{order}", order_id = order } },
                intention_order_id = order,
                client_secret = $"egy_csk_test_{order}",
            });
        }

        Requests.Enqueue((path, body));
        if (path == PaymobPaths.Pay)
        {
            return body!["source"]!["subtype"]!.GetValue<string>() == "WALLET"
                ? Json(HttpStatusCode.OK, new { pending = true, redirect_url = "https://accept.paymob.test/wallet/otp" })
                : Json(HttpStatusCode.OK, new { pending = true, data = new { bill_reference = 7788991 } });
        }

        return new HttpResponseMessage(HttpStatusCode.NotFound);
    }

    private static HttpResponseMessage Json(HttpStatusCode status, object body) => new(status)
    {
        Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json"),
    };
}
