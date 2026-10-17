namespace Stockly.Domain.Platform;

/// <summary>Outcome of one AI call or cache lookup (docs/design/backend.md §1.3). Stored as <c>smallint</c>; values only appended.</summary>
public enum AiCallOutcome
{
    Succeeded = 1,

    /// <summary>HTTP 2xx whose content failed strict parsing or validation.</summary>
    InvalidOutput = 2,

    /// <summary>HTTP 429.</summary>
    RateLimited = 3,

    /// <summary>HTTP 5xx.</summary>
    ServerError = 4,

    /// <summary>Any other HTTP 4xx.</summary>
    ClientError = 5,

    Timeout = 6,

    NetworkError = 7,

    /// <summary>The job was stopped while the call ran.</summary>
    Cancelled = 8,
}
