using System.Text.Json;
using Stockly.Application.Abstractions.Ai;
using Stockly.Domain.Platform;

namespace Stockly.Api.IntegrationTests.Support;

/// <summary>The AI of an Api test host: never OpenRouter. Answers with one readable line unless a test sets <see cref="Result"/>.</summary>
public sealed class FakeExtractor : IAiInvoiceExtractor
{
    public const string Model = "fake/vision-model";

    private int _calls;

    public int Calls => _calls;

    public AiExtractionResult Result { get; set; } = OneLine();

    public Task<AiExtractionResult> ExtractAsync(AiExtractionRequest request, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _calls);
        return Task.FromResult(Result);
    }

    public static AiExtractionResult OneLine()
    {
        var extraction = new ExtractedInvoice(
            "شركة الأدوية", "INV-1", "2026-10-10", "125.00", false, [new ExtractedInvoiceLine("بنادول اكسترا", "علبة", "10", "12.50", "125.00", null, null)]);
        return new AiExtractionResult(
            extraction,
            null,
            Model,
            JsonSerializer.Serialize(extraction, AiJson.Options),
            [new AiAttempt(Model, AiCallOutcome.Succeeded, 200, 1000, 200, 0.0004m, 1200)]);
    }
}
