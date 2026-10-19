using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Stockly.Infrastructure.Payments.Paymob;

/// <summary>
/// The HMAC of a Paymob transaction processed callback (developers.paymob.com, "HMAC Transaction Callback"): the values
/// of <see cref="Fields"/> of the callback's <c>obj</c>, in that order, concatenated without separators (booleans as
/// <c>true</c>/<c>false</c>, numbers as sent, a missing or null value as an empty string), signed with HMAC-SHA512 and
/// the merchant's HMAC secret, hex-encoded in lower case. Compared in constant time.
/// </summary>
public static class PaymobHmac
{
    /// <summary>The documented key order. <c>id</c> is the transaction id (<c>obj.id</c> in a processed callback).</summary>
    public static readonly IReadOnlyList<string> Fields =
    [
        "amount_cents",
        "created_at",
        "currency",
        "error_occured",
        "has_parent_transaction",
        "id",
        "integration_id",
        "is_3d_secure",
        "is_auth",
        "is_capture",
        "is_refunded",
        "is_standalone_payment",
        "is_voided",
        "order.id",
        "owner",
        "pending",
        "source_data.pan",
        "source_data.sub_type",
        "source_data.type",
        "success",
    ];

    /// <summary>The lower-case hex HMAC-SHA512 of <paramref name="transaction"/> (the callback's <c>obj</c>).</summary>
    public static string Compute(JsonObject transaction, string secret)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentException.ThrowIfNullOrEmpty(secret);

        var message = new StringBuilder();
        foreach (var field in Fields)
        {
            message.Append(ValueOf(transaction, field));
        }

        var hash = HMACSHA512.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(message.ToString()));
        return Convert.ToHexStringLower(hash);
    }

    /// <summary>Whether <paramref name="received"/> (hex, any case) is the HMAC of <paramref name="transaction"/>.</summary>
    public static bool Verify(JsonObject transaction, string secret, string? received)
    {
        if (string.IsNullOrWhiteSpace(received))
        {
            return false;
        }

        var expected = Encoding.ASCII.GetBytes(Compute(transaction, secret));
        var actual = Encoding.ASCII.GetBytes(received.Trim().ToLowerInvariant());
        return CryptographicOperations.FixedTimeEquals(expected, actual);
    }

    private static string ValueOf(JsonObject transaction, string path)
    {
        JsonNode? node = transaction;
        foreach (var segment in path.Split('.'))
        {
            node = node is JsonObject parent ? parent[segment] : null;
        }

        return node is JsonValue value ? Text(value) : string.Empty;
    }

    private static string Text(JsonValue value) => value.GetValueKind() switch
    {
        JsonValueKind.String => value.GetValue<string>(),
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        JsonValueKind.Number => value.ToJsonString(),
        _ => string.Empty,
    };

    /// <summary>A JSON value as invariant text (ids may come as numbers or strings).</summary>
    internal static string? AsText(JsonNode? node) => node is JsonValue value
        ? value.GetValueKind() switch
        {
            JsonValueKind.String => value.GetValue<string>(),
            JsonValueKind.Number => value.ToJsonString(),
            _ => null,
        }
        : null;

    /// <summary>A JSON boolean (or the strings <c>true</c>/<c>false</c>); <c>false</c> otherwise.</summary>
    internal static bool AsBool(JsonNode? node) => node is JsonValue value && value.GetValueKind() switch
    {
        JsonValueKind.True => true,
        JsonValueKind.String => bool.TryParse(value.GetValue<string>(), out var parsed) && parsed,
        _ => false,
    };

    /// <summary>A JSON integer (or a string of digits); <c>null</c> otherwise.</summary>
    internal static long? AsLong(JsonNode? node) =>
        long.TryParse(AsText(node), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : null;
}
