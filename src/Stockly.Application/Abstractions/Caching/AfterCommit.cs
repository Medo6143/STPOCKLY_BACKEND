namespace Stockly.Application.Abstractions.Caching;

/// <summary>
/// The token for cache writes made after a transaction committed (version bumps, status snapshots, list removals).
/// The change is already saved, so a client that disconnects (or a job that is stopped) at this point must not skip
/// them: a cancelled bump throws <see cref="OperationCanceledException"/> out of the Redis adapter, the client retries,
/// gets the existing resource back, and the stale lists stay cached until their TTL (AGENTS.md rules 23, 25). This is
/// the one deliberate exception to rule 29; every Redis call still has its own timeout.
/// </summary>
public static class AfterCommit
{
    public static CancellationToken Token => CancellationToken.None;
}
