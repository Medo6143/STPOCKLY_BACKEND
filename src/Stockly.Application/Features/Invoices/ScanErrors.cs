using Stockly.Domain.Common;

namespace Stockly.Application.Features.Invoices;

/// <summary>Scan limits (contract I1). Both carry <c>Retry-After</c>.</summary>
public static class ScanErrors
{
    public const string RateLimitedCode = "Scan.RateLimited";
    public const string DailyQuotaExceededCode = "Scan.DailyQuotaExceeded";

    /// <summary>The shop sent more scans this minute than <c>Scan:RateLimitPerMinute</c>.</summary>
    public static Error RateLimited(TimeSpan retryAfter) =>
        Error.TooManyRequests(RateLimitedCode, "Too many scans this minute.") with { RetryAfter = retryAfter };

    /// <summary>The plan's scans for today are used up; they reset at Africa/Cairo midnight.</summary>
    public static Error DailyQuotaExceeded(TimeSpan retryAfter) =>
        Error.TooManyRequests(DailyQuotaExceededCode, "The daily scan quota is used up.") with { RetryAfter = retryAfter };
}
