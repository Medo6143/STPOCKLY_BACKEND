using System.ComponentModel.DataAnnotations;

namespace Stockly.Infrastructure.Jobs;

/// <summary>Configuration section <c>Hangfire</c> (docs/design/backend.md §14). Validated at startup.</summary>
public sealed class HangfireOptions
{
    public const string SectionName = "Hangfire";

    /// <summary>The PostgreSQL schema of Hangfire's tables (created by the <c>InvoiceScan</c> migration).</summary>
    public const string Schema = "hangfire";

    /// <summary>The queue of invoice jobs.</summary>
    public const string InvoicesQueue = "invoices";

    /// <summary>Workers on the <see cref="InvoicesQueue"/> queue in this process (<c>Hangfire__WorkerCount</c>).</summary>
    [Range(1, 50)]
    public int WorkerCount { get; set; } = 2;
}
