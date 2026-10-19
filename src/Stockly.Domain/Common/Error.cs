using System.Diagnostics.CodeAnalysis;

namespace Stockly.Domain.Common;

/// <summary>An expected failure. <see cref="Code"/> is stable and machine-readable, e.g. <c>Product.NameRequired</c>.</summary>
[SuppressMessage(
    "Naming",
    "CA1716:Identifiers should not match keywords",
    Justification = "'Error' is only a keyword in VB.NET; Stockly is C#-only and the name is part of the agreed Result/Error design.")]
public sealed record Error(string Code, string Message, ErrorType Type)
{
    /// <summary>Per-field messages for request-shape failures, keyed by camelCase field path (e.g. <c>shop.name</c>).</summary>
    public IReadOnlyDictionary<string, string[]>? FieldErrors { get; init; }

    /// <summary>How long the caller should wait before trying again (<c>Retry-After</c>), for temporary failures.</summary>
    public TimeSpan? RetryAfter { get; init; }

    public static Error Validation(string code, string message) => new(code, message, ErrorType.Validation);

    public static Error NotFound(string code, string message) => new(code, message, ErrorType.NotFound);

    public static Error Conflict(string code, string message) => new(code, message, ErrorType.Conflict);

    public static Error Forbidden(string code, string message) => new(code, message, ErrorType.Forbidden);

    public static Error Unauthorized(string code, string message) => new(code, message, ErrorType.Unauthorized);

    public static Error Locked(string code, string message) => new(code, message, ErrorType.Locked);

    public static Error TooManyRequests(string code, string message) => new(code, message, ErrorType.TooManyRequests);

    public static Error PayloadTooLarge(string code, string message) => new(code, message, ErrorType.PayloadTooLarge);

    public static Error BadGateway(string code, string message) => new(code, message, ErrorType.BadGateway);
}
