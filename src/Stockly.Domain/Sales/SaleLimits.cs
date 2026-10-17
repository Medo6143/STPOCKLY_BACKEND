namespace Stockly.Domain.Sales;

/// <summary>Limits of a sale from the contract (S1).</summary>
public static class SaleLimits
{
    /// <summary>At most 100 lines per sale; <c>line_no</c> is 1..100.</summary>
    public const int MaxItems = 100;

    /// <summary>Longest sale note.</summary>
    public const int NoteMaxLength = 500;
}
