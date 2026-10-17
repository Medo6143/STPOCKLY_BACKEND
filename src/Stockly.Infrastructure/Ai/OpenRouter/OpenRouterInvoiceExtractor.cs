using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentValidation;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly.Timeout;
using Stockly.Application.Abstractions.Ai;
using Stockly.Domain.Invoices;
using Stockly.Domain.Platform;
using Stockly.Infrastructure.Ai.OpenRouter.Dtos;
using Stockly.Infrastructure.Ai.OpenRouter.Prompts;

namespace Stockly.Infrastructure.Ai.OpenRouter;

/// <summary>
/// <see cref="IAiInvoiceExtractor"/> on OpenRouter <c>chat/completions</c> (docs/design/backend.md §5, §6). It only
/// talks to OpenRouter (SOLID S): the typed client's pipeline retries 429/5xx and times out each attempt; this class
/// builds the request, tries the primary model and then the fallback model (§6.2), parses the output strictly and
/// validates it (rule 15). Called only by the background job (rule 14). Never logs the key, the image or the content.
/// </summary>
internal sealed partial class OpenRouterInvoiceExtractor(
    HttpClient http,
    IOptions<OpenRouterOptions> options,
    IValidator<ExtractedInvoice> validator,
    ILogger<OpenRouterInvoiceExtractor> logger) : IAiInvoiceExtractor
{
    private const string ChatCompletions = "chat/completions";
    private const string JsonObject = "json_object";
    private const string SystemRole = "system";
    private const string UserRole = "user";
    private const string TextPart = "text";
    private const string ImagePart = "image_url";
    private const string LengthFinish = "length";
    private const string Bearer = "Bearer";
    private const string RefererHeader = "HTTP-Referer";
    private const string TitleHeader = "X-Title";

    private static readonly JsonSerializerOptions RequestJson = new() { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

    // Key, credit and permission errors are the same for every model: the fallback would fail the same way.
    private static readonly HashSet<HttpStatusCode> StopStatuses =
        [HttpStatusCode.Unauthorized, HttpStatusCode.PaymentRequired, HttpStatusCode.Forbidden];

    public async Task<AiExtractionResult> ExtractAsync(AiExtractionRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var settings = options.Value;
        LogExtracting(logger, request.Image.Length, request.ContentType);

        var attempts = new List<AiAttempt>();
        string? lastContent = null;
        var anyOutput = false;
        var anyValidButEmpty = false;
        foreach (var model in new[] { settings.Model, settings.FallbackModel })
        {
            var call = await CallAsync(settings, model, request, cancellationToken);
            attempts.AddRange(call.Attempts);
            lastContent = call.Content ?? lastContent;

            switch (call.Kind)
            {
                case CallKind.Usable:
                    return new AiExtractionResult(call.Extraction, null, model, call.Content, attempts);
                case CallKind.Stop:
                    return new AiExtractionResult(null, InvoiceFailureReason.AiUnavailable, null, lastContent, attempts);
                case CallKind.ValidButEmpty:
                    anyValidButEmpty = true;
                    anyOutput = true;
                    break;
                case CallKind.InvalidOutput:
                    anyOutput = true;
                    break;
                default:
                    break;
            }
        }

        var failure = anyValidButEmpty ? InvoiceFailureReason.AiNoItems
            : anyOutput ? InvoiceFailureReason.AiOutputInvalid
            : InvoiceFailureReason.AiUnavailable;
        return new AiExtractionResult(null, failure, null, lastContent, attempts);
    }

    private async Task<ModelCall> CallAsync(
        OpenRouterOptions settings, string model, AiExtractionRequest request, CancellationToken cancellationToken)
    {
        var log = new AiAttemptLog(model, cancellationToken);
        using var message = BuildRequest(settings, model, request);
        message.Options.Set(AiAttemptLog.Key, log);

        HttpStatusCode status;
        string body;
        try
        {
            using var response = await http.SendAsync(message, cancellationToken);
            status = response.StatusCode;
            body = response.IsSuccessStatusCode ? await response.Content.ReadAsStringAsync(cancellationToken) : string.Empty;
        }
        catch (Exception exception) when (exception is TimeoutRejectedException or HttpRequestException
            || (exception is OperationCanceledException && !cancellationToken.IsCancellationRequested))
        {
            // Timeouts and network errors are not retried by the pipeline; they go to the fallback model.
            return ModelCall.Transport(ToAttempts(log, null));
        }

        if (StopStatuses.Contains(status))
        {
            return new ModelCall(CallKind.Stop, null, null, ToAttempts(log, null));
        }

        if (!IsSuccess(status))
        {
            return ModelCall.Transport(ToAttempts(log, null));
        }

        return Read(model, body, log);
    }

    private ModelCall Read(string model, string body, AiAttemptLog log)
    {
        OpenRouterChatResponse? response;
        try
        {
            response = JsonSerializer.Deserialize<OpenRouterChatResponse>(body);
        }
        catch (JsonException)
        {
            response = null;
        }

        var usage = response?.Usage;
        var choice = response?.Choices is [var first, ..] ? first : null;
        var content = choice?.Message?.Content;
        if (response is null || response.Error is not null || string.IsNullOrWhiteSpace(content) || choice!.FinishReason == LengthFinish)
        {
            return Invalid(model, content, log, usage);
        }

        ExtractedInvoice extraction;
        try
        {
            extraction = AiJson.Parse(content);
        }
        catch (JsonException)
        {
            return Invalid(model, content, log, usage);
        }

        if (!validator.Validate(extraction).IsValid)
        {
            return Invalid(model, content, log, usage);
        }

        var attempts = ToAttempts(log, usage);
        LogOutput(logger, model, extraction.Items.Count, usage?.PromptTokens ?? 0, usage?.CompletionTokens ?? 0, usage?.Cost ?? 0m);
        return extraction.Items.Count == 0
            ? new ModelCall(CallKind.ValidButEmpty, null, content, attempts)
            : new ModelCall(CallKind.Usable, extraction, content, attempts);
    }

    private ModelCall Invalid(string model, string? content, AiAttemptLog log, OpenRouterUsage? usage)
    {
        LogInvalidOutput(logger, model);
        return new ModelCall(CallKind.InvalidOutput, null, content, ToAttempts(log, usage, AiCallOutcome.InvalidOutput));
    }

    // Tokens and cost come from the final body, so they belong to the last attempt; earlier attempts had no usable body.
    private static List<AiAttempt> ToAttempts(AiAttemptLog log, OpenRouterUsage? usage, AiCallOutcome? lastOutcome = null)
    {
        var entries = log.Entries;
        return [.. entries.Select((entry, index) => index == entries.Count - 1
            ? new AiAttempt(
                log.Model,
                lastOutcome ?? entry.Outcome,
                entry.HttpStatus,
                usage?.PromptTokens ?? 0,
                usage?.CompletionTokens ?? 0,
                usage?.Cost ?? 0m,
                entry.LatencyMs)
            : new AiAttempt(log.Model, entry.Outcome, entry.HttpStatus, 0, 0, 0m, entry.LatencyMs))];
    }

    private static HttpRequestMessage BuildRequest(OpenRouterOptions settings, string model, AiExtractionRequest request)
    {
        var imageUrl = $"data:{request.ContentType};base64,{Convert.ToBase64String(request.Image.Span)}";
        var body = new OpenRouterChatRequest(
            model,
            [
                new OpenRouterMessage(SystemRole, [new OpenRouterContentPart(TextPart, InvoiceExtractionPrompt.System, null)]),
                new OpenRouterMessage(
                    UserRole,
                    [
                        new OpenRouterContentPart(TextPart, InvoiceExtractionPrompt.User, null),
                        new OpenRouterContentPart(ImagePart, null, new OpenRouterImageUrl(imageUrl)),
                    ]),
            ],
            Temperature: 0m,
            new OpenRouterResponseFormat(JsonObject),
            settings.MaxOutputTokens,
            new OpenRouterUsageRequest(Include: true));

        var message = new HttpRequestMessage(HttpMethod.Post, new Uri(ChatCompletions, UriKind.Relative))
        {
            Content = JsonContent.Create(body, options: RequestJson),
        };
        message.Headers.Authorization = new AuthenticationHeaderValue(Bearer, settings.ApiKey);
        if (!string.IsNullOrWhiteSpace(settings.AppUrl))
        {
            message.Headers.TryAddWithoutValidation(RefererHeader, settings.AppUrl);
        }

        if (!string.IsNullOrWhiteSpace(settings.AppTitle))
        {
            message.Headers.TryAddWithoutValidation(TitleHeader, settings.AppTitle);
        }

        return message;
    }

    private static bool IsSuccess(HttpStatusCode status) => (int)status is >= 200 and < 300;

    [LoggerMessage(EventId = 41, Level = LogLevel.Information, Message = "Extracting an invoice image of {ImageBytes} bytes ({ContentType})")]
    private static partial void LogExtracting(ILogger logger, int imageBytes, string contentType);

    [LoggerMessage(
        EventId = 42,
        Level = LogLevel.Information,
        Message = "OpenRouter {Model} returned {ItemCount} items ({PromptTokens} prompt and {CompletionTokens} completion tokens, cost {CostUsd} USD)")]
    private static partial void LogOutput(ILogger logger, string model, int itemCount, int promptTokens, int completionTokens, decimal costUsd);

    [LoggerMessage(EventId = 43, Level = LogLevel.Warning, Message = "OpenRouter {Model} returned output that failed parsing or validation")]
    private static partial void LogInvalidOutput(ILogger logger, string model);

    private enum CallKind
    {
        Usable,
        ValidButEmpty,
        InvalidOutput,
        Transport,
        Stop,
    }

    private sealed record ModelCall(CallKind Kind, ExtractedInvoice? Extraction, string? Content, IReadOnlyList<AiAttempt> Attempts)
    {
        public static ModelCall Transport(IReadOnlyList<AiAttempt> attempts) => new(CallKind.Transport, null, null, attempts);
    }
}
