using NSubstitute;
using Stockly.Application.Abstractions.Persistence;
using Stockly.Application.Features.Invoices.Validation;
using Stockly.Domain.Invoices;

namespace Stockly.Application.Tests.Features.Invoices.Validation;

public sealed class DuplicateInvoiceNumberRuleTests
{
    private readonly IInvoiceRepository _invoices = Substitute.For<IInvoiceRepository>();

    [Fact]
    public async Task Evaluate_SameNumberAndSupplierExists_DuplicateInvoiceNumber()
    {
        var supplierId = Guid.CreateVersion7();
        _invoices.ExistsOtherWithNumberAsync(RuleContext.InvoiceId, supplierId, "INV-1", Arg.Any<CancellationToken>()).Returns(true);
        var context = RuleContext.At(InvoiceValidationStage.Extraction) with { InvoiceNumber = "INV-1", SupplierId = supplierId };

        (await new DuplicateInvoiceNumberRule(_invoices).EvaluateAsync(context, CancellationToken.None))
            .Should().Be(InvoiceWarning.DuplicateInvoiceNumber);
    }

    [Fact]
    public async Task Evaluate_NoOtherInvoiceWithTheNumber_NoWarning()
    {
        var context = RuleContext.At(InvoiceValidationStage.Review) with { InvoiceNumber = "INV-2" };

        (await new DuplicateInvoiceNumberRule(_invoices).EvaluateAsync(context, CancellationToken.None)).Should().BeNull();
        await _invoices.Received(1).ExistsOtherWithNumberAsync(RuleContext.InvoiceId, null, "INV-2", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Evaluate_NoInvoiceNumber_NoQuery()
    {
        (await new DuplicateInvoiceNumberRule(_invoices).EvaluateAsync(RuleContext.At(InvoiceValidationStage.Extraction), CancellationToken.None))
            .Should().BeNull();
        await _invoices.DidNotReceive().ExistsOtherWithNumberAsync(
            Arg.Any<Guid>(), Arg.Any<Guid?>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }
}
