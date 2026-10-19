using Stockly.Domain.Shops;

namespace Stockly.Application.Abstractions.Persistence;

public interface IPlanRepository
{
    /// <summary>The id of the active plan with this code, or <c>null</c>.</summary>
    Task<Guid?> FindActiveIdByCodeAsync(string code, CancellationToken cancellationToken);

    /// <summary>The plan (global, not tracked); <c>null</c> when it does not exist.</summary>
    Task<Plan?> FindAsync(Guid planId, CancellationToken cancellationToken);
}
