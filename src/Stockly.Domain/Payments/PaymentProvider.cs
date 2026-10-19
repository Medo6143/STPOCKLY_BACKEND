namespace Stockly.Domain.Payments;

/// <summary>
/// The payment provider that holds a payment (ADR-0015 §1). Another provider is a new value and a new gateway class.
/// Stored as smallint: values are never reordered, only appended.
/// </summary>
public enum PaymentProvider
{
    Paymob = 1,
}
