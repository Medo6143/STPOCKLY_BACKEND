using Stockly.Application.Features.Invoices.Validation;
using Stockly.Domain.Invoices;

namespace Stockly.Application.Tests.Features.Invoices.Validation;

public sealed class ItemCapExceededRuleTests
{
    private readonly ItemCapExceededRule _rule = new();

    [Fact]
    public async Task Evaluate_ModelTruncated_ItemCapExceeded()
    {
        (await _rule.EvaluateAsync(RuleContext.At(InvoiceValidationStage.Extraction) with { Truncated = true }, CancellationToken.None))
            .Should().Be(InvoiceWarning.ItemCapExceeded);
    }

    [Fact]
    public async Task Evaluate_NotTruncated_NoWarning()
    {
        (await _rule.EvaluateAsync(RuleContext.At(InvoiceValidationStage.Extraction), CancellationToken.None)).Should().BeNull();
    }

    [Fact]
    public void Stages_ExtractionOnly()
    {
        _rule.Stages.Should().Be(InvoiceValidationStage.Extraction);
    }
}
