using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Stockly.Application.Abstractions.Admin;
using Stockly.Application.Abstractions.Time;

namespace Stockly.Infrastructure.Jobs;

/// <summary>
/// One-time Hangfire dashboard codes in this process's memory (contract §11.3): 32 random bytes (base64url), stored only
/// as their SHA-256, expiring after <see cref="HangfireSession.CodeLifetime"/> (by <see cref="IClock"/>), removed on first
/// redemption. Assumes one API instance (decision of Sprint 5; several instances would keep the codes in Redis).
/// Singleton.
/// </summary>
public sealed class InMemoryHangfireSessionCodes(IClock clock) : IHangfireSessionCodes
{
    private const int CodeBytes = 32;

    private readonly ConcurrentDictionary<string, Issued> _codes = new(StringComparer.Ordinal);

    public string Issue(Guid adminUserId)
    {
        if (adminUserId == Guid.Empty)
        {
            throw new ArgumentException("A dashboard code belongs to an admin.", nameof(adminUserId));
        }

        RemoveExpired();
        var code = System.Buffers.Text.Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(CodeBytes));
        _codes[Hash(code)] = new Issued(adminUserId, clock.UtcNow + HangfireSession.CodeLifetime);
        return code;
    }

    public Guid? Redeem(string code)
    {
        if (string.IsNullOrEmpty(code) || !_codes.TryRemove(Hash(code), out var issued))
        {
            return null;
        }

        return clock.UtcNow < issued.ExpiresAt ? issued.AdminUserId : null;
    }

    private static string Hash(string code) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(code)));

    private void RemoveExpired()
    {
        var now = clock.UtcNow;
        foreach (var (key, issued) in _codes)
        {
            if (issued.ExpiresAt <= now)
            {
                _codes.TryRemove(key, out _);
            }
        }
    }

    private sealed record Issued(Guid AdminUserId, DateTimeOffset ExpiresAt);
}
