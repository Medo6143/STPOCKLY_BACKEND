namespace Stockly.Domain.Common;

/// <summary>Kind of an expected failure. The Api maps each value to an HTTP status code.</summary>
public enum ErrorType
{
    Validation = 1,
    NotFound = 2,
    Conflict = 3,
    Forbidden = 4,
    Unauthorized = 5,
    Failure = 6,
}
