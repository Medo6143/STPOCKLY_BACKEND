using System.Text.Json.Serialization;

namespace Stockly.Infrastructure.Ai.OpenRouter.Dtos;

/// <summary>A <c>chat/completions</c> response (docs/design/backend.md §5.3). Only the fields Stockly reads.</summary>
internal sealed record OpenRouterChatResponse(
    [property: JsonPropertyName("id")] string? Id,
    [property: JsonPropertyName("model")] string? Model,
    [property: JsonPropertyName("choices")] IReadOnlyList<OpenRouterChoice>? Choices,
    [property: JsonPropertyName("usage")] OpenRouterUsage? Usage,
    [property: JsonPropertyName("error")] OpenRouterError? Error);

internal sealed record OpenRouterChoice(
    [property: JsonPropertyName("message")] OpenRouterResponseMessage? Message,
    [property: JsonPropertyName("finish_reason")] string? FinishReason);

/// <summary>The model's answer. <see cref="Content"/> is untrusted and never logged (AGENTS.md rule 12).</summary>
internal sealed record OpenRouterResponseMessage(
    [property: JsonPropertyName("role")] string? Role,
    [property: JsonPropertyName("content")] string? Content);

/// <summary>Token counts and the cost in USD credits.</summary>
internal sealed record OpenRouterUsage(
    [property: JsonPropertyName("prompt_tokens")] int PromptTokens,
    [property: JsonPropertyName("completion_tokens")] int CompletionTokens,
    [property: JsonPropertyName("total_tokens")] int TotalTokens,
    [property: JsonPropertyName("cost")] decimal? Cost);

/// <summary>An error reported inside a 200 body. <see cref="Message"/> is never logged.</summary>
internal sealed record OpenRouterError(
    [property: JsonPropertyName("code")] int? Code,
    [property: JsonPropertyName("message")] string? Message);
