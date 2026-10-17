using Stockly.Domain.Shops;

namespace Stockly.Application.Abstractions.Admin;

/// <summary>Plans for the platform admin (AD8, AD11–AD13). Used only by <c>Application/Features/Admin</c>.</summary>
public interface IAdminPlanRepository
{
    /// <summary>Every plan, active first, then by code (a small table, not paged). No tracking.</summary>
    Task<IReadOnlyList<Plan>> ListAsync(CancellationToken cancellationToken);

    /// <summary>The plan, tracked; <c>null</c> when it does not exist.</summary>
    Task<Plan?> FindAsync(Guid planId, CancellationToken cancellationToken);

    Task<bool> CodeExistsAsync(string code, CancellationToken cancellationToken);

    void Add(Plan plan);
}
