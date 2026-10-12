# ADR-0007: Redis usage and version-stamped cache keys

- Status: Accepted (2026-10-12)
- Date: 2026-10-12

## Decision
Redis serves four separate jobs behind four interfaces: read cache, AI result cache, scan rate limit and quota, invoice status for polling. Catalog cache keys embed a per-shop version number; writes `INCR` the version after commit instead of deleting keys by pattern. Redis failure degrades to Postgres and never fails a request.

Details, key catalogue and TTLs: `docs/design/redis.md`.

## Consequences
- O(1) invalidation with no `SCAN`.
- Old entries live until TTL; memory cost is small at shop scale.
