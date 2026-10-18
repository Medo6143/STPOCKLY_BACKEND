using System.Text.Json;

namespace Stockly.Admin.Services;

/// <summary>Reads a failed response into an <see cref="ApiProblem"/>. Never touches request headers (tokens).</summary>
public static class ProblemReader
{
    private const string CodeProperty = "code";
    private const string TitleProperty = "title";
    private const string DetailProperty = "detail";
    private const string TraceIdProperty = "traceId";
    private const string ErrorsProperty = "errors";

    public static async Task<ApiProblem> ReadAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(response);

        JsonElement body;
        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            body = document.RootElement.Clone();
        }
        catch (JsonException)
        {
            return ApiProblem.FromStatus(response.StatusCode, null);
        }

        if (body.ValueKind != JsonValueKind.Object)
        {
            return ApiProblem.FromStatus(response.StatusCode, null);
        }

        var traceId = ReadString(body, TraceIdProperty);
        var code = ReadString(body, CodeProperty);
        if (code is null)
        {
            return ApiProblem.FromStatus(response.StatusCode, traceId);
        }

        return new ApiProblem(
            (int)response.StatusCode,
            code,
            ReadString(body, TitleProperty) ?? response.StatusCode.ToString(),
            ReadString(body, DetailProperty),
            traceId,
            ReadErrors(body));
    }

    private static Dictionary<string, string[]> ReadErrors(JsonElement body)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        if (!body.TryGetProperty(ErrorsProperty, out var element) || element.ValueKind != JsonValueKind.Object)
        {
            return errors;
        }

        foreach (var field in element.EnumerateObject().Where(field => field.Value.ValueKind == JsonValueKind.Array))
        {
            errors[field.Name] = field.Value.EnumerateArray()
                .Where(message => message.ValueKind == JsonValueKind.String)
                .Select(message => message.GetString() ?? string.Empty)
                .ToArray();
        }

        return errors;
    }

    private static string? ReadString(JsonElement body, string name) =>
        body.TryGetProperty(name, out var element) && element.ValueKind == JsonValueKind.String ? element.GetString() : null;
}
