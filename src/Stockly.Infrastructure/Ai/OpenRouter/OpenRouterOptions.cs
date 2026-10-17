using System.ComponentModel.DataAnnotations;

namespace Stockly.Infrastructure.Ai.OpenRouter;

/// <summary>
/// Configuration section <c>OpenRouter</c> (docs/design/backend.md §14, AGENTS.md rule 17). The API key is a secret from
/// <c>.env</c> or user-secrets (rule 11) and is never logged. Validated at startup.
/// </summary>
public sealed class OpenRouterOptions
{
    public const string SectionName = "OpenRouter";

    [Required]
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>Ends with a slash, so <c>chat/completions</c> resolves under it.</summary>
    [Required]
    public Uri? BaseUrl { get; set; }

    /// <summary>The primary vision model.</summary>
    [Required]
    public string Model { get; set; } = string.Empty;

    /// <summary>The model called when the primary one fails or returns nothing usable.</summary>
    [Required]
    public string FallbackModel { get; set; } = string.Empty;

    /// <summary>Time limit of one HTTP attempt.</summary>
    [Range(1, 120)]
    public int TimeoutSeconds { get; set; } = 30;

    /// <summary>Retries per model, on HTTP 429 and 5xx only.</summary>
    [Range(0, 2)]
    public int MaxRetries { get; set; } = 2;

    /// <summary>The request's <c>max_tokens</c>.</summary>
    [Range(256, 64_000)]
    public int MaxOutputTokens { get; set; } = 8000;

    /// <summary>Sent as <c>HTTP-Referer</c> when set.</summary>
    public string? AppUrl { get; set; }

    /// <summary>Sent as <c>X-Title</c> when set.</summary>
    public string? AppTitle { get; set; }
}
