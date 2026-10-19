namespace Stockly.Domain.Payments;

/// <summary>
/// How the owner pays (ADR-0015 §2). The client chooses a method, never a provider. Stored as smallint: values are never
/// reordered, only appended.
/// </summary>
public enum PaymentMethod
{
    /// <summary>Visa, Mastercard, Meeza on the provider's hosted page.</summary>
    Card = 1,

    /// <summary>Vodafone Cash and other mobile wallets.</summary>
    Wallet = 2,

    /// <summary>Cash at a kiosk partner against a reference number.</summary>
    Kiosk = 3,
}
