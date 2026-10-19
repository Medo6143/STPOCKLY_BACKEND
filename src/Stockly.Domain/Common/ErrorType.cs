namespace Stockly.Domain.Common;

/// <summary>Kind of an expected failure. The Api maps each value to an HTTP status code.
/// New values are only appended, never reordered.</summary>
public enum ErrorType
{
    Validation = 1,
    NotFound = 2,
    Conflict = 3,
    Forbidden = 4,
    Unauthorized = 5,
    Failure = 6,

    /// <summary>The resource is temporarily locked, e.g. an account after too many failed logins (423).</summary>
    Locked = 7,

    /// <summary>The caller sent too many requests (429).</summary>
    TooManyRequests = 8,

    /// <summary>The request body is larger than allowed, e.g. an invoice image over 8 MB (413).</summary>
    PayloadTooLarge = 9,

    /// <summary>An upstream provider (e.g. the payment provider) failed; nothing was changed (502).</summary>
    BadGateway = 10,
}
