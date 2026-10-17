using Hangfire.Common;

namespace Stockly.Infrastructure.Jobs;

/// <summary>
/// Hangfire's own filters (job attributes such as <c>[Queue]</c> and <c>[AutomaticRetry]</c>, and its defaults) plus
/// <see cref="InvoiceJobFailureFilter"/>. Given to this process's server instead of adding to Hangfire's static global
/// filters, so each host (and each test host) keeps its own.
/// </summary>
internal sealed class StocklyJobFilterProvider(InvoiceJobFailureFilter failureFilter) : IJobFilterProvider
{
    public IEnumerable<JobFilter> GetFilters(Job job) =>
        JobFilterProviders.Providers.GetFilters(job).Append(new JobFilter(failureFilter, JobFilterScope.Global, order: null));
}
