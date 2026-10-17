using Stockly.Application.Features.Invoices.Validation;

namespace Stockly.Application.Tests.Features.Invoices.Validation;

/// <summary>A context without any reason for a warning; tests change one field with <c>with</c>.</summary>
internal static class RuleContext
{
    public static readonly Guid InvoiceId = Guid.Parse("0192f0a0-0000-7000-8000-0000000000d1");

    public static InvoiceValidationContext At(InvoiceValidationStage stage) =>
        new(stage, InvoiceId, SupplierId: null, InvoiceNumber: null, TotalAmount: null, ComputedTotal: 100m, LineCount: 3, UnreadableLineCount: 0, Truncated: false);
}
