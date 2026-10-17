using System.ComponentModel.DataAnnotations;

namespace Stockly.Application.Features.Invoices;

/// <summary>Configuration section <c>Scan</c> (docs/design/backend.md §14). Validated at startup.</summary>
public sealed class ScanOptions
{
    public const string SectionName = "Scan";

    /// <summary>Scans per shop per minute (<c>Scan__RateLimitPerMinute</c>).</summary>
    [Range(1, 1000)]
    public int RateLimitPerMinute { get; set; } = 10;

    /// <summary>
    /// A repeat of a scan still <c>Processing</c> after this long queues its job again (<c>Scan__RequeueAfterSeconds</c>).
    /// Longer than the worst-case job (about 200 s).
    /// </summary>
    [Range(60, 86_400)]
    public int RequeueAfterSeconds { get; set; } = 600;
}
