namespace Stockly.Domain.Payments;

/// <summary>
/// State of a payment (contract §12.1). <c>Pending</c> → <c>Paid</c> | <c>Failed</c> | <c>Expired</c>; <c>Paid</c> is
/// final. A provider success on a <c>Failed</c> or <c>Expired</c> payment still makes it <c>Paid</c> (the money was taken).
/// Stored as smallint: values are never reordered, only appended.
/// </summary>
public enum PaymentStatus
{
    Pending = 1,
    Paid = 2,
    Failed = 3,
    Expired = 4,
}
