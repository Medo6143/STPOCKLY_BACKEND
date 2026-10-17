using Microsoft.EntityFrameworkCore;
using Stockly.Application.Abstractions.Admin;
using Stockly.Domain.Shops;

namespace Stockly.Infrastructure.Persistence.Admin;

/// <summary>Plans for the platform admin. Plans are global (no tenant filter); the table is small.</summary>
internal sealed class AdminPlanRepository(StocklyDbContext db) : IAdminPlanRepository
{
    public async Task<IReadOnlyList<Plan>> ListAsync(CancellationToken cancellationToken) =>
        await db.Set<Plan>().AsNoTracking()
            .OrderByDescending(plan => plan.IsActive)
            .ThenBy(plan => plan.Code)
            .ToListAsync(cancellationToken);

    public Task<Plan?> FindAsync(Guid planId, CancellationToken cancellationToken) =>
        db.Set<Plan>().SingleOrDefaultAsync(plan => plan.Id == planId, cancellationToken);

    // ux_plans_code.
    public Task<bool> CodeExistsAsync(string code, CancellationToken cancellationToken) =>
        db.Set<Plan>().AsNoTracking().AnyAsync(plan => plan.Code == code, cancellationToken);

    public void Add(Plan plan) => db.Set<Plan>().Add(plan);
}
