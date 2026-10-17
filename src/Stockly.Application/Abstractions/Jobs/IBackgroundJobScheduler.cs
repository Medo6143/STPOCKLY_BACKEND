namespace Stockly.Application.Abstractions.Jobs;

/// <summary>
/// Queues background work (Hangfire in Infrastructure). Job arguments are ids only, never image bytes or tokens
/// (AGENTS.md rule 12).
/// </summary>
public interface IBackgroundJobScheduler
{
    /// <summary>Queues the extraction of <paramref name="invoiceId"/> for <paramref name="shopId"/>. Safe to call twice: the job is idempotent.</summary>
    void EnqueueProcessInvoice(Guid shopId, Guid invoiceId);
}
