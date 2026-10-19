using System.ComponentModel.DataAnnotations;

namespace Stockly.Infrastructure.Payments.Paymob;

/// <summary>
/// Configuration section <c>Paymob</c> (ADR-0015 §8, docs/features/billing.md §9). Every key, the HMAC secret and the
/// integration ids come from <c>.env</c> only (AGENTS.md rule 11) and are never logged. Payments are disabled while
/// <see cref="SecretKey"/> is empty; the other secrets are then not required. Validated at startup.
/// </summary>
public sealed class PaymobOptions : IValidatableObject
{
    public const string SectionName = "Paymob";

    /// <summary>Intention API key (<c>Authorization: Token …</c>). Empty disables payments.</summary>
    public string? SecretKey { get; set; }

    /// <summary>Public key of the Unified Checkout URL.</summary>
    public string? PublicKey { get; set; }

    /// <summary>The HMAC secret that signs callbacks.</summary>
    public string? HmacSecret { get; set; }

    /// <summary>API key for an auth token, used by the transaction inquiry of the reconcile job.</summary>
    public string? ApiKey { get; set; }

    /// <summary>Paymob API base address, ending with a slash.</summary>
    [Required]
    public Uri? BaseUrl { get; set; } = new("https://accept.paymob.com/");

    /// <summary>Unified Checkout page of the region (Egypt), ending with a slash.</summary>
    [Required]
    public Uri? CheckoutBaseUrl { get; set; } = new("https://eg.checkout.paymob.com/");

    /// <summary>The integration id of each method; a method without one is not configured (<c>Billing.MethodUnavailable</c>).</summary>
    public PaymobIntegrationIds IntegrationIds { get; set; } = new();

    /// <summary>Where Paymob sends the payer's browser after a card or wallet payment (<c>redirection_url</c>).</summary>
    public Uri? RedirectionUrl { get; set; }

    /// <summary>
    /// Our callback, <c>POST /api/payments/paymob/callback</c> on the public API address (<c>notification_url</c>). When
    /// empty, the callback set on the integration in the Paymob dashboard is used.
    /// </summary>
    public Uri? NotificationUrl { get; set; }

    /// <summary>Card and wallet: minutes until a pending payment expires.</summary>
    [Range(5, 24 * 60)]
    public int PendingExpiryMinutes { get; set; } = 30;

    /// <summary>Kiosk: hours until an unpaid reference expires (Paymob does not return an expiry).</summary>
    [Range(1, 30 * 24)]
    public int KioskExpiryHours { get; set; } = 48;

    /// <summary>Time limit of one Paymob call.</summary>
    [Range(1, 120)]
    public int TimeoutSeconds { get; set; } = 30;

    /// <summary>Whether payments are enabled (a secret key is set).</summary>
    public bool Enabled => !string.IsNullOrWhiteSpace(SecretKey);

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (!Enabled)
        {
            yield break;
        }

        if (string.IsNullOrWhiteSpace(PublicKey))
        {
            yield return Missing(nameof(PublicKey));
        }

        if (string.IsNullOrWhiteSpace(HmacSecret))
        {
            yield return Missing(nameof(HmacSecret));
        }

        if (string.IsNullOrWhiteSpace(ApiKey))
        {
            yield return Missing(nameof(ApiKey));
        }
    }

    /// <summary>The integration id configured for <paramref name="method"/>, or <c>null</c>.</summary>
    public int? IntegrationIdFor(Domain.Payments.PaymentMethod method) => method switch
    {
        Domain.Payments.PaymentMethod.Card => IntegrationIds.Card,
        Domain.Payments.PaymentMethod.Wallet => IntegrationIds.Wallet,
        Domain.Payments.PaymentMethod.Kiosk => IntegrationIds.Kiosk,
        _ => null,
    };

    private static ValidationResult Missing(string member) =>
        new("Required when Paymob__SecretKey is set (payments enabled).", [member]);
}

/// <summary><c>Paymob__IntegrationIds__Card</c>, <c>__Wallet</c>, <c>__Kiosk</c>.</summary>
public sealed class PaymobIntegrationIds
{
    [Range(1, int.MaxValue)]
    public int? Card { get; set; }

    [Range(1, int.MaxValue)]
    public int? Wallet { get; set; }

    [Range(1, int.MaxValue)]
    public int? Kiosk { get; set; }
}
