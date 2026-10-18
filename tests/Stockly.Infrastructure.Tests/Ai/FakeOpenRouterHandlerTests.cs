using FluentValidation;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Stockly.Application.Abstractions.Ai;
using Stockly.Application.Features.Invoices.ProcessInvoice;
using Stockly.Domain.Platform;
using Stockly.Infrastructure.Ai.OpenRouter;

namespace Stockly.Infrastructure.Tests.Ai;

/// <summary>
/// The load test's OpenRouter stand-in through the production wiring (<c>AddOpenRouter(useFakeAi: true)</c>): the
/// extractor gets a valid extraction without any network call (the base URL points nowhere).
/// </summary>
public sealed class FakeOpenRouterHandlerTests
{
    private const string Primary = "google/gemini-2.5-flash";

    [Fact]
    public async Task Extract_WithTheFakeHandler_ValidThreeLineExtractionAndOneSucceededAttempt()
    {
        await using var provider = Provider(useFakeAi: true);
        var extractor = provider.GetRequiredService<IAiInvoiceExtractor>();

        var result = await extractor.ExtractAsync(new AiExtractionRequest(new byte[] { 0xFF, 0xD8, 0xFF, 0x01 }, "image/jpeg"), CancellationToken.None);

        result.Failure.Should().BeNull();
        result.Model.Should().Be(Primary);
        result.Extraction!.Items.Should().HaveCount(3);
        result.Extraction.TotalAmount.Should().Be("425.00");
        result.Attempts.Should().ContainSingle().Which.Should().BeEquivalentTo(
            new { Model = Primary, Outcome = AiCallOutcome.Succeeded, HttpStatus = 200, PromptTokens = 1000, CompletionTokens = 200, CostUsd = 0m });
    }

    [Fact]
    public async Task Extract_WithoutTheFakeHandler_GoesToTheConfiguredUrl()
    {
        await using var provider = Provider(useFakeAi: false);
        var extractor = provider.GetRequiredService<IAiInvoiceExtractor>();

        var result = await extractor.ExtractAsync(new AiExtractionRequest(new byte[] { 0xFF, 0xD8, 0xFF, 0x01 }, "image/jpeg"), CancellationToken.None);

        result.Failure.Should().NotBeNull("nothing listens on port 1: the real handler was used");
    }

    [Theory]
    [InlineData(null, 0)]
    [InlineData("", 0)]
    [InlineData("0", 0)]
    [InlineData("8000", 8000)]
    [InlineData("20000", 20000)]
    public void ReadLatency_ValidOrUnset_Milliseconds(string? value, int expected)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [FakeOpenRouterHandler.LatencyKey] = value })
            .Build();

        FakeOpenRouterHandler.ReadLatency(configuration).Should().Be(TimeSpan.FromMilliseconds(expected));
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("20001")]
    [InlineData("1.5")]
    [InlineData("soon")]
    public void ReadLatency_Invalid_FailsWithVariableName(string value)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [FakeOpenRouterHandler.LatencyKey] = value })
            .Build();

        var act = () => FakeOpenRouterHandler.ReadLatency(configuration);

        act.Should().Throw<InvalidOperationException>().WithMessage("*Ai__FakeLatencyMs*");
    }

    [Fact]
    public async Task Extract_WithLatency_WaitsBeforeAnswering()
    {
        using var handler = new FakeOpenRouterHandler(TimeSpan.FromMilliseconds(300));
        using var client = new HttpClient(handler);
        var watch = System.Diagnostics.Stopwatch.StartNew();

        using var response = await client.PostAsync(new Uri("http://127.0.0.1:1/chat/completions"), new StringContent("{}"));

        watch.Elapsed.Should().BeGreaterThanOrEqualTo(TimeSpan.FromMilliseconds(250));
        response.IsSuccessStatusCode.Should().BeTrue();
        (await response.Content.ReadAsStringAsync()).Should().Contain(FakeOpenRouterHandler.DefaultModel);
    }

    private static ServiceProvider Provider(bool useFakeAi)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["OpenRouter:ApiKey"] = "test-only-openrouter-key",
                ["OpenRouter:BaseUrl"] = "http://127.0.0.1:1/api/v1/",
                ["OpenRouter:Model"] = Primary,
                ["OpenRouter:FallbackModel"] = "openai/gpt-4.1-mini",
                ["OpenRouter:TimeoutSeconds"] = "1",
                ["OpenRouter:MaxRetries"] = "0",
            })
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IValidator<ExtractedInvoice>, ExtractedInvoiceValidator>();
        services.AddOpenRouter(configuration, useFakeAi);
        return services.BuildServiceProvider(validateScopes: true);
    }
}
