using Stockly.Domain.Common;

namespace Stockly.Domain.Users;

/// <summary>
/// One refresh token of a user (AGENTS.md rule 13). Only the SHA-256 hash of the token is stored. Each login or
/// registration (one device session) starts a family whose <see cref="FamilyId"/> is the id of its first token.
/// Every refresh revokes the presented token and issues its successor in the same family
/// (<see cref="ReplacedById"/> chains them). Presenting a revoked token revokes the whole family.
/// </summary>
public sealed class RefreshToken : Entity
{
    // An empty familyId starts a new family led by this token. EF passes the stored value.
    private RefreshToken(Guid userId, string tokenHash, Guid familyId, string? deviceId, DateTimeOffset expiresAt, DateTimeOffset createdAt)
    {
        UserId = userId;
        TokenHash = tokenHash;
        FamilyId = familyId == Guid.Empty ? Id : familyId;
        DeviceId = deviceId;
        ExpiresAt = expiresAt;
        CreatedAt = createdAt;
    }

    public Guid UserId { get; }

    /// <summary>Lower-case hex SHA-256 of the token. The token itself is never stored.</summary>
    public string TokenHash { get; }

    public Guid FamilyId { get; }

    public string? DeviceId { get; }

    public DateTimeOffset ExpiresAt { get; }

    public DateTimeOffset? RevokedAt { get; private set; }

    /// <summary>The token issued when this one was rotated.</summary>
    public Guid? ReplacedById { get; private set; }

    public DateTimeOffset CreatedAt { get; }

    public bool IsRevoked => RevokedAt is not null;

    /// <summary>Starts a new family (one device session).</summary>
    public static RefreshToken StartFamily(Guid userId, string tokenHash, string? deviceId, DateTimeOffset now, TimeSpan lifetime)
    {
        if (userId == Guid.Empty)
        {
            throw new ArgumentException("A refresh token must belong to a user.", nameof(userId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(tokenHash);
        return new RefreshToken(userId, tokenHash, familyId: Guid.Empty, Clean(deviceId), now + lifetime, now);
    }

    public bool IsExpired(DateTimeOffset now) => now >= ExpiresAt;

    /// <summary>Revokes this token and returns its successor in the same family, for the same device.</summary>
    public RefreshToken Rotate(string newTokenHash, DateTimeOffset now, TimeSpan lifetime)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(newTokenHash);
        if (IsRevoked)
        {
            throw new InvalidOperationException("A revoked refresh token cannot be rotated.");
        }

        var successor = new RefreshToken(UserId, newTokenHash, FamilyId, DeviceId, now + lifetime, now);
        RevokedAt = now;
        ReplacedById = successor.Id;
        return successor;
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
