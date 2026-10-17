using Stockly.Application.Features.Invoices.Validation;
using Stockly.Domain.Invoices;

namespace Stockly.Application.Tests.Features.Invoices.Validation;

public sealed class UnreadableLinesRuleTests
{
    private readonly UnreadableLinesRule _rule = new();

    [Fact]
    public async Task Evaluate_SomeLinesUnreadable_UnreadableLines()
    {
        (await _rule.EvaluateAsync(RuleContext.At(InvoiceValidationStage.Extraction) with { UnreadableLineCount = 2 }, CancellationToken.None))
            .Should().Be(InvoiceWarning.UnreadableLines);
    }

    [Fact]
    public async Task Evaluate_AllLinesRead_NoWarning()
    {
        (await _rule.EvaluateAsync(RuleContext.At(InvoiceValidationStage.Extraction), CancellationToken.None)).Should().BeNull();
    }

    [Fact]
    public void Stages_ExtractionOnly()
    {
        _rule.Stages.Should().Be(InvoiceValidationStage.Extraction);
    }
}
