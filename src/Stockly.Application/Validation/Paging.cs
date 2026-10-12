namespace Stockly.Application.Validation;

/// <summary>Paging rules of the contract (§1.3, AGENTS.md rule 30): <c>pageSize &gt; 100</c> is rejected, never clamped.</summary>
public static class Paging
{
    public const int DefaultPage = 1;
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 100;
}
