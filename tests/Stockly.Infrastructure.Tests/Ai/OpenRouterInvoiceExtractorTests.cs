using System.Net;
using System.Text.Json;
using FluentValidation;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Logging;
using Stockly.Application.Abstractions.Ai;
using Stockly.Application.Features.Invoices.ProcessInvoice;
using Stockly.Domain.Invoices;
using Stockly.Domain.Platform;
using Stockly.Infrastructure.Tests.Support;
using static Stockly.Infrastructure.Tests.Ai.FakeOpenRouter;

namespace Stockly.Infrastructure.Tests.Ai;

/// <summary>
/// <c>OpenRouterInvoiceExtractor</c> through the production wiring (<c>AddOpenRouter</c>: typed client, retry, timeout,
/// attempt recorder) against a fake <see cref="HttpMessageHandler"/> (AGENTS.md §8). Never calls OpenRouter.
/// </summary>
public sealed class OpenRouterInvoiceExtractorTests : IDisposable
{
    private const string Primary = "google/gemini-2.5-flash";
    private const string Fallback = "openai/gpt-4.1-mini";
    private const string ApiKey = "test-only-openrouter-key";

    private static readonly AiExtractionRequest Request = new(new byte[] { 0xFF, 0xD8, 0xFF, 0x01 }, "image/jpeg");

    private readonly FakeOpenRouter _openRouter = new();
    private readonly ListLoggerProvider _logs = new();
    private readonly ServiceProvider _provider;

    public OpenRouterInvoiceExtractorTests()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["OpenRouter:ApiKey"] = ApiKey,
                ["OpenRouter:BaseUrl"] = "https://openrouter.test/api/v1/",
                ["OpenRouter:Model"] = Primary,
                ["OpenRouter:FallbackModel"] = Fallback,
                ["OpenRouter:TimeoutSeconds"] = "1",
                ["OpenRouter:AppTitle"] = "Stockly",
            })
            .Build();
        var services = new ServiceCollection();
        services.AddLogging(logging => logging.AddProvider(_logs));
        services.AddSingleton<IValidator<ExtractedInvoice>, ExtractedInvoiceValidator>();
        services.AddOpenRouter(configuration);
        services.ConfigureAll<HttpClientFactoryOptions>(options =>
            options.HttpMessageHandlerBuilderActions.Add(builder => builder.PrimaryHandler = _openRouter));
        _provider = services.BuildServiceProvider(validateScopes: true);
    }

    [Fact]
    public async Task Extract_ValidJson_SucceedsOnTheFirstAttemptWithTokensAndCost()
    {
        _openRouter.Answer(Primary, Completion(Output(Line("بنادول"))));

        var result = await ExtractAsync();

        result.Failure.Should().BeNull();
        result.Model.Should().Be(Primary);
        result.Extraction!.Items.Should().ContainSingle().Which.Name.Should().Be("بنادول");
        result.RawContent.Should().Be(Output(Line("بنادول")));
        result.Attempts.Should().ContainSingle().Which.Should().BeEquivalentTo(
            new { Model = Primary, Outcome = AiCallOutcome.Succeeded, HttpStatus = 200, PromptTokens = 1000, CompletionTokens = 200, CostUsd = 0.00042m });
        _openRouter.CountFor(Fallback).Should().Be(0);
    }

    [Fact]
    public async Task Extract_Request_FollowsRule17AndCarriesTheImageAsDataUrl()
    {
        _openRouter.Answer(Primary, Completion(Output(Line("بنادول"))));

        await ExtractAsync();

        var request = _openRouter.Requests.Single();
        request.Uri.Should().Be(new Uri("https://openrouter.test/api/v1/chat/completions"));
        request.Headers["Authorization"].Should().Be($"Bearer {ApiKey}");
        request.Headers["X-Title"].Should().Be("Stockly");
        request.Headers.Should().NotContainKey("HTTP-Referer", "AppUrl is not configured");
        request.Body["temperature"]!.GetValue<decimal>().Should().Be(0m);
        request.Body["response_format"]!["type"]!.GetValue<string>().Should().Be("json_object");
        request.Body["max_tokens"]!.GetValue<int>().Should().Be(8000);
        request.Body["usage"]!["include"]!.GetValue<bool>().Should().BeTrue();
        request.Body["messages"]![0]!["role"]!.GetValue<string>().Should().Be("system");
        request.Body["messages"]![1]!["content"]![1]!["image_url"]!["url"]!.GetValue<string>()
            .Should().Be($"data:image/jpeg;base64,{Convert.ToBase64String(Request.Image.Span)}");
        request.Body["messages"]![1]!["content"]![0]!["image_url"].Should().BeNull("null properties are not sent");
    }

    [Fact]
    public async Task Extract_MalformedJsonFromBothModels_AiOutputInvalidWithBothAttempts()
    {
        _openRouter.Answer(Primary, Completion("{ not json")).Answer(Fallback, Completion("```json {} ```"));

        var result = await ExtractAsync();

        result.Failure.Should().Be(InvoiceFailureReason.AiOutputInvalid);
        result.Extraction.Should().BeNull();
        result.RawContent.Should().Be("```json {} ```", "the last output seen is kept for raw_ai_json");
        result.Attempts.Select(attempt => (attempt.Model, attempt.Outcome))
            .Should().Equal((Primary, AiCallOutcome.InvalidOutput), (Fallback, AiCallOutcome.InvalidOutput));
        result.Attempts.Should().OnlyContain(attempt => attempt.PromptTokens == 1000, "invalid output was still paid for");
    }

    [Fact]
    public async Task Extract_PrimaryInvalidFallbackValid_FallbackModelUsed()
    {
        _openRouter.Answer(Primary, Completion("{\"wrong\":1}")).Answer(Fallback, Completion(Output(Line("كونجستال"))));

        var result = await ExtractAsync();

        result.Model.Should().Be(Fallback);
        result.Extraction!.Items.Single().Name.Should().Be("كونجستال");
        result.Attempts.Select(attempt => (attempt.Model, attempt.Outcome))
            .Should().Equal((Primary, AiCallOutcome.InvalidOutput), (Fallback, AiCallOutcome.Succeeded));
    }

    [Fact]
    public async Task Extract_EmptyItemsFromBoth_AiNoItems()
    {
        _openRouter.Answer(Primary, Completion(Output())).Answer(Fallback, Completion(Output()));

        var result = await ExtractAsync();

        result.Failure.Should().Be(InvoiceFailureReason.AiNoItems);
        result.Attempts.Should().HaveCount(2).And.OnlyContain(attempt => attempt.Outcome == AiCallOutcome.Succeeded);
    }

    [Fact]
    public async Task Extract_EmptyItemsThenInvalid_StillAiNoItems()
    {
        _openRouter.Answer(Primary, Completion(Output())).Answer(Fallback, Completion("nope"));

        (await ExtractAsync()).Failure.Should().Be(InvoiceFailureReason.AiNoItems);
    }

    [Fact]
    public async Task Extract_MoreThanHundredItems_InvalidOutput()
    {
        var tooMany = Output([.. Enumerable.Range(0, 101).Select(i => Line($"item {i}"))]);
        _openRouter.Answer(Primary, Completion(tooMany)).Answer(Fallback, Completion(tooMany));

        var result = await ExtractAsync();

        result.Failure.Should().Be(InvoiceFailureReason.AiOutputInvalid);
    }

    [Fact]
    public async Task Extract_RateLimited_RetriedTwiceThenFallback()
    {
        _openRouter.Answer(Primary, Status(HttpStatusCode.TooManyRequests)).Answer(Fallback, Completion(Output(Line("بنادول"))));

        var result = await ExtractAsync();

        _openRouter.CountFor(Primary).Should().Be(3, "one attempt and at most 2 retries (rule 17)");
        result.Model.Should().Be(Fallback);
        result.Attempts.Select(attempt => (attempt.Model, attempt.Outcome, attempt.HttpStatus)).Should().Equal(
            (Primary, AiCallOutcome.RateLimited, 429),
            (Primary, AiCallOutcome.RateLimited, 429),
            (Primary, AiCallOutcome.RateLimited, 429),
            (Fallback, AiCallOutcome.Succeeded, 200));
    }

    [Fact]
    public async Task Extract_ServerErrorThenSuccess_RetriedOnTheSameModel()
    {
        _openRouter.Answer(Primary, Status(HttpStatusCode.BadGateway), Completion(Output(Line("بنادول"))));

        var result = await ExtractAsync();

        result.Model.Should().Be(Primary);
        result.Attempts.Select(attempt => attempt.Outcome).Should().Equal(AiCallOutcome.ServerError, AiCallOutcome.Succeeded);
        result.Attempts[1].PromptTokens.Should().Be(1000);
        result.Attempts[0].PromptTokens.Should().Be(0);
    }

    [Fact]
    public async Task Extract_ServerErrorsEverywhere_AiUnavailableWithSixAttempts()
    {
        _openRouter.Answer(Primary, Status(HttpStatusCode.InternalServerError)).Answer(Fallback, Status(HttpStatusCode.ServiceUnavailable));

        var result = await ExtractAsync();

        result.Failure.Should().Be(InvoiceFailureReason.AiUnavailable);
        result.Attempts.Should().HaveCount(6).And.OnlyContain(attempt => attempt.Outcome == AiCallOutcome.ServerError);
    }

    [Fact]
    public async Task Extract_Timeout_NotRetriedFallbackUsed()
    {
        _openRouter.Answer(Primary, Hang()).Answer(Fallback, Completion(Output(Line("بنادول"))));

        var result = await ExtractAsync();

        _openRouter.CountFor(Primary).Should().Be(1, "timeouts are not retried (rule 17: 429/5xx only)");
        result.Model.Should().Be(Fallback);
        result.Attempts[0].Should().BeEquivalentTo(new { Model = Primary, Outcome = AiCallOutcome.Timeout, HttpStatus = (int?)null });
        result.Attempts[0].LatencyMs.Should().BeGreaterThanOrEqualTo(900);
    }

    [Fact]
    public async Task Extract_NetworkError_NotRetriedFallbackUsed()
    {
        _openRouter.Answer(Primary, NetworkError()).Answer(Fallback, Completion(Output(Line("بنادول"))));

        var result = await ExtractAsync();

        _openRouter.CountFor(Primary).Should().Be(1);
        result.Attempts.Select(attempt => attempt.Outcome).Should().Equal(AiCallOutcome.NetworkError, AiCallOutcome.Succeeded);
    }

    [Fact]
    public async Task Extract_BadRequest_NotRetriedFallbackUsed()
    {
        _openRouter.Answer(Primary, Status(HttpStatusCode.BadRequest)).Answer(Fallback, Completion(Output(Line("بنادول"))));

        var result = await ExtractAsync();

        result.Attempts.Select(attempt => (attempt.Outcome, attempt.HttpStatus))
            .Should().Equal((AiCallOutcome.ClientError, 400), (AiCallOutcome.Succeeded, 200));
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.PaymentRequired)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task Extract_KeyCreditOrPermissionError_StopsWithoutFallback(HttpStatusCode status)
    {
        _openRouter.Answer(Primary, Status(status)).Answer(Fallback, Completion(Output(Line("بنادول"))));

        var result = await ExtractAsync();

        result.Failure.Should().Be(InvoiceFailureReason.AiUnavailable);
        result.Attempts.Should().ContainSingle().Which.Outcome.Should().Be(AiCallOutcome.ClientError);
        _openRouter.CountFor(Fallback).Should().Be(0, "the fallback would fail the same way");
    }

    [Theory]
    [InlineData("""{"error":{"code":502,"message":"provider down"}}""")]
    [InlineData("""{"id":"x","choices":[]}""")]
    [InlineData("not a json body")]
    public async Task Extract_OkWithoutUsableChoice_InvalidOutput(string body)
    {
        _openRouter.Answer(Primary, Body(body)).Answer(Fallback, Completion(Output(Line("بنادول"))));

        var result = await ExtractAsync();

        result.Attempts[0].Outcome.Should().Be(AiCallOutcome.InvalidOutput);
        result.Model.Should().Be(Fallback);
    }

    [Fact]
    public async Task Extract_CutOffByLength_InvalidOutput()
    {
        _openRouter.Answer(Primary, Completion(Output(Line("بنادول")), finishReason: "length")).Answer(Fallback, Completion(Output()));

        var result = await ExtractAsync();

        result.Attempts[0].Outcome.Should().Be(AiCallOutcome.InvalidOutput);
        result.Failure.Should().Be(InvoiceFailureReason.AiNoItems);
    }

    [Fact]
    public async Task Extract_Logs_NeverContainTheKeyTheImageOrTheContent()
    {
        _openRouter.Answer(Primary, Status(HttpStatusCode.TooManyRequests)).Answer(Fallback, Completion(Output(Line("بنادول"))));

        await ExtractAsync();

        var text = string.Join('\n', _logs.Entries.Select(entry => entry.Message));
        text.Should().Contain(Primary).And.Contain("429");
        text.Should().NotContain(ApiKey).And.NotContain("base64").And.NotContain("بنادول").And.NotContain("secret detail");
    }

    [Fact]
    public async Task Extract_JobCancelled_Throws()
    {
        _openRouter.Answer(Primary, Hang());
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        var act = () => _provider.GetRequiredService<IAiInvoiceExtractor>().ExtractAsync(Request, cancellation.Token);

        await act.Should().ThrowAsync<OperationCanceledException>("a stopped job is retried by Hangfire, not failed");
    }

    public void Dispose()
    {
        _provider.Dispose();
        _openRouter.Dispose();
        _logs.Dispose();
    }

    private Task<AiExtractionResult> ExtractAsync() =>
        _provider.GetRequiredService<IAiInvoiceExtractor>().ExtractAsync(Request, CancellationToken.None);

    private static ExtractedInvoiceLine Line(string name) => new(name, "علبة", "1", "10.00", "10.00", null, null);

    private static string Output(params ExtractedInvoiceLine[] lines) =>
        JsonSerializer.Serialize(new ExtractedInvoice(null, "INV-1", null, null, false, lines), AiJson.Options);
}
