using Stockly.Domain.Users;

namespace Stockly.Application.Abstractions.Persistence;

/// <summary>Refresh tokens belong to a user, not a shop (no tenant filter). Looked up only by the hash of a presented token.</summary>
public interface IRefreshTokenRepository
{
    void Add(RefreshToken token);

    /// <summary>Loads the token for rotation and locks its row (<c>FOR UPDATE</c>) until the transaction ends.</summary>
    Task<RefreshToken?> FindByHashForUpdateAsync(string tokenHash, CancellationToken cancellationToken);

    /// <summary>Read-only lookup (logout).</summary>
    Task<RefreshToken?> FindByHashAsync(string tokenHash, CancellationToken cancellationToken);

    /// <summary>Revokes every not-yet-revoked token of the family, including its active one. Other families are untouched.</summary>
    Task RevokeFamilyAsync(Guid familyId, DateTimeOffset revokedAt, CancellationToken cancellationToken);
}
