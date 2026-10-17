using NSubstitute;
using Stockly.Application.Abstractions.Persistence;
using Stockly.Application.Features.Invoices.Validation;
using Stockly.Domain.Invoices;

namespace Stockly.Application.Tests.Features.Invoices.Validation;

public sealed class InvoiceValidatorTests
{
    private readonly IInvoiceRepository _invoices = Substitute.For<IInvoiceRepository>();

    [Fact]
    public async Task Validate_Extraction_RunsEveryRuleByOrder()
    {
        _invoices.ExistsOtherWithNumberAsync(Arg.Any<Guid>(), Arg.Any<Guid?>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(true);
        var context = RuleContext.At(InvoiceValidationStage.Extraction) with
        {
            Truncated = true,
            UnreadableLineCount = 1,
            TotalAmount = 50m,
            InvoiceNumber = "INV-1",
        };

        var warnings = await Validator().ValidateAsync(context, CancellationToken.None);

        warnings.Should().Equal(
            InvoiceWarning.ItemCapExceeded, InvoiceWarning.UnreadableLines, InvoiceWarning.TotalMismatch, InvoiceWarning.DuplicateInvoiceNumber);
    }

    [Fact]
    public async Task Validate_Review_SkipsExtractionOnlyRules()
    {
        var context = RuleContext.At(InvoiceValidationStage.Review) with { Truncated = true, UnreadableLineCount = 4, TotalAmount = 50m };

        var warnings = await Validator().ValidateAsync(context, CancellationToken.None);

        warnings.Should().Equal(InvoiceWarning.TotalMismatch);
    }

    [Fact]
    public async Task Validate_NothingWrong_NoWarnings()
    {
        (await Validator().ValidateAsync(RuleContext.At(InvoiceValidationStage.Extraction), CancellationToken.None)).Should().BeEmpty();
    }

    // Registered out of order on purpose: the validator sorts by Order.
    private InvoiceValidator Validator() =>
        new([new DuplicateInvoiceNumberRule(_invoices), new TotalMismatchRule(), new ItemCapExceededRule(), new UnreadableLinesRule()]);
}
