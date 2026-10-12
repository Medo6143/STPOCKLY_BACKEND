using Microsoft.EntityFrameworkCore;
using Stockly.Application.Abstractions.Persistence;
using Stockly.Domain.Users;
using Stockly.Infrastructure.Persistence.Configurations;

namespace Stockly.Infrastructure.Persistence.Repositories;

internal sealed class RefreshTokenRepository(StocklyDbContext db) : IRefreshTokenRepository
{
    private const string SelectForUpdateByHash =
        "SELECT * FROM " + RefreshTokenConfiguration.TableName + " WHERE token_hash = {0} FOR UPDATE";

    public void Add(RefreshToken token) => db.Set<RefreshToken>().Add(token);

    public async Task<RefreshToken?> FindByHashForUpdateAsync(string tokenHash, CancellationToken cancellationToken)
    {
        // Not composed further, so the row lock is exactly this statement's. Tracked: the caller rotates it.
        var rows = await db.Set<RefreshToken>()
            .FromSqlRaw(SelectForUpdateByHash, tokenHash)
            .ToListAsync(cancellationToken);
        return rows.SingleOrDefault();
    }

    public Task<RefreshToken?> FindByHashAsync(string tokenHash, CancellationToken cancellationToken) =>
        db.Set<RefreshToken>().AsNoTracking().SingleOrDefaultAsync(token => token.TokenHash == tokenHash, cancellationToken);

    public Task RevokeFamilyAsync(Guid familyId, DateTimeOffset revokedAt, CancellationToken cancellationToken) =>
        db.Set<RefreshToken>()
            .Where(token => token.FamilyId == familyId && token.RevokedAt == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(token => token.RevokedAt, revokedAt), cancellationToken);
}
