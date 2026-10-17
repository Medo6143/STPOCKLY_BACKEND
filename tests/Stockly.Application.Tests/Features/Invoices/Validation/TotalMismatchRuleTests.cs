using Stockly.Application.Features.Invoices.Validation;
using Stockly.Domain.Invoices;

namespace Stockly.Application.Tests.Features.Invoices.Validation;

/// <summary>AGENTS.md rule 16 at both stages.</summary>
public sealed class TotalMismatchRuleTests
{
    private readonly TotalMismatchRule _rule = new();

    [Theory]
    [InlineData(InvoiceValidationStage.Extraction)]
    [InlineData(InvoiceValidationStage.Review)]
    public async Task Evaluate_MoreThanTwoPercentOff_TotalMismatch(InvoiceValidationStage stage)
    {
        var context = RuleContext.At(stage) with { TotalAmount = 100m, ComputedTotal = 102.01m };

        (await _rule.EvaluateAsync(context, CancellationToken.None)).Should().Be(InvoiceWarning.TotalMismatch);
    }

    [Fact]
    public async Task Evaluate_WithinTwoPercent_NoWarning()
    {
        var context = RuleContext.At(InvoiceValidationStage.Extraction) with { TotalAmount = 100m, ComputedTotal = 98m };

        (await _rule.EvaluateAsync(context, CancellationToken.None)).Should().BeNull();
    }

    [Fact]
    public async Task Evaluate_NoPrintedTotal_NoWarning()
    {
        (await _rule.EvaluateAsync(RuleContext.At(InvoiceValidationStage.Extraction), CancellationToken.None)).Should().BeNull();
    }

    [Fact]
    public void Stages_ExtractionAndReview()
    {
        _rule.Stages.Should().Be(InvoiceValidationStage.Extraction | InvoiceValidationStage.Review);
    }
}
