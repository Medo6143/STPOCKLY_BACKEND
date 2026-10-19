using Microsoft.EntityFrameworkCore;
using Stockly.Application.Abstractions.Persistence;
using Stockly.Domain.Shops;

namespace Stockly.Infrastructure.Persistence.Repositories;

internal sealed class PlanRepository(StocklyDbContext db) : IPlanRepository
{
    public Task<Guid?> FindActiveIdByCodeAsync(string code, CancellationToken cancellationToken) =>
        db.Set<Plan>().AsNoTracking()
            .Where(plan => plan.Code == code && plan.IsActive)
            .Select(plan => (Guid?)plan.Id)
            .SingleOrDefaultAsync(cancellationToken);

    // Primary key.
    public Task<Plan?> FindAsync(Guid planId, CancellationToken cancellationToken) =>
        db.Set<Plan>().AsNoTracking().SingleOrDefaultAsync(plan => plan.Id == planId, cancellationToken);
}
