namespace Stockly.Application.Abstractions.Jobs;

/// <summary>
/// Queues background work (Hangfire in Infrastructure). Job arguments are ids only, never image bytes or tokens
/// (AGENTS.md rule 12).
/// </summary>
public interface IBackgroundJobScheduler
{
    /// <summary>Queues the extraction of <paramref name="invoiceId"/> for <paramref name="shopId"/>. Safe to call twice: the job is idempotent.</summary>
    void EnqueueProcessInvoice(Guid shopId, Guid invoiceId);

    /// <summary>
    /// Queues the extraction of a failed invoice again for the platform admin (contract AD19): the run skips the AI result
    /// cache and the reuse of an earlier extraction, so OpenRouter is called. Quota is never charged by a job.
    /// </summary>
    void EnqueueRerunInvoice(Guid shopId, Guid invoiceId);
}
