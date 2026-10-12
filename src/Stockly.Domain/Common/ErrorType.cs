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
}
