using System.Text.Json.Serialization;

namespace Stockly.Infrastructure.Ai.OpenRouter.Dtos;

/// <summary>Body of <c>POST chat/completions</c> (docs/design/backend.md §5.2). Serialized without null properties.</summary>
internal sealed record OpenRouterChatRequest(
    [property: JsonPropertyName("model")] string Model,
    [property: JsonPropertyName("messages")] IReadOnlyList<OpenRouterMessage> Messages,
    [property: JsonPropertyName("temperature")] decimal Temperature,
    [property: JsonPropertyName("response_format")] OpenRouterResponseFormat ResponseFormat,
    [property: JsonPropertyName("max_tokens")] int MaxTokens,
    [property: JsonPropertyName("usage")] OpenRouterUsageRequest Usage);

/// <summary><c>{"type": "json_object"}</c> (AGENTS.md rule 17).</summary>
internal sealed record OpenRouterResponseFormat([property: JsonPropertyName("type")] string Type);

/// <summary><c>{"include": true}</c>: the response then reports <c>usage.cost</c>.</summary>
internal sealed record OpenRouterUsageRequest([property: JsonPropertyName("include")] bool Include);

/// <summary>A <c>system</c> or <c>user</c> message made of content parts.</summary>
internal sealed record OpenRouterMessage(
    [property: JsonPropertyName("role")] string Role,
    [property: JsonPropertyName("content")] IReadOnlyList<OpenRouterContentPart> Content);

/// <summary>A <c>text</c> part or an <c>image_url</c> part.</summary>
internal sealed record OpenRouterContentPart(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("text")] string? Text,
    [property: JsonPropertyName("image_url")] OpenRouterImageUrl? ImageUrl);

/// <summary>The image as a data URL: <c>data:{contentType};base64,{bytes}</c>. Never logged.</summary>
internal sealed record OpenRouterImageUrl([property: JsonPropertyName("url")] string Url);
