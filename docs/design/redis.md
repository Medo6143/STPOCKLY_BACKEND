# Redis in Stockly

Status: **Draft for review** · ADR: 0007

Redis has four jobs in Stockly. Each one sits behind its own small Application interface (ISP), because they need different Redis features:

| Job | Interface (Application) | Implementation (Infrastructure) | Redis feature |
|---|---|---|---|
| Read cache | `ICacheService` | `RedisCacheService` over `IDistributedCache` | GET/SET with TTL |
| AI result cache | `IAiResultCache` | `RedisAiResultCache` | GET/SET, 7-day TTL |
| Rate limit + daily quota | `IScanRateLimiter`, `IScanQuotaCounter` | `RedisScanLimiter` over `IConnectionMultiplexer` | atomic `INCR` + `EXPIRE` (Lua) |
| Invoice status for polling | `IInvoiceStatusCache` | `RedisInvoiceStatusCache` | GET/SET, short TTL |

`IDistributedCache` has no atomic counter, so the limiter uses StackExchange.Redis directly. This is why the cache and the limiter are separate interfaces and not one `IRedisService`.

## 1. Key catalogue

Prefix rule: tenant data is always `stockly:{shopId}:...`. Platform-wide data uses `stockly:platform:...` (the only exception, used by the admin dashboard).

| Key | Value | TTL | Written by | Invalidated by |
|---|---|---|---|---|
| `stockly:{shopId}:ver:catalog` | integer version | none (tiny) | — | `INCR` on any product/stock write, after commit: P3–P10 (`CatalogCacheInvalidationBehavior`), I5 confirm and S1 sale (their handlers, after their own commit, only when they wrote) |
| `stockly:{shopId}:products:list:v{ver}:{queryHash}` | paged product DTOs | 10 min | `GetProductsQueryHandler` | version bump |
| `stockly:{shopId}:products:lowstock:v{ver}:{queryHash}` | low-stock page DTOs (paged, rule 30; `queryHash` covers search and paging) | 5 min | `GetLowStockQueryHandler` | version bump |
| `stockly:{shopId}:dashboard:summary:v{ver}:{yyyyMMdd}` | D1 `DashboardSummary` DTO; `{yyyyMMdd}` is the Africa/Cairo date, so a value cached before local midnight is not served after it | 2 min | `GetDashboardSummaryQueryHandler` (D1, Sprint 4; cache skipped when the version cannot be read) | version bump (every product, unit, stock, confirm and sale write, after commit); the next Cairo day |
| `stockly:{shopId}:suppliers:list` | supplier DTOs | 30 min | `GetSuppliersQueryHandler` (not built yet) | `DEL` on supplier write: `ConfirmInvoiceCommandHandler` after its commit when it created a supplier (`ICacheService.RemoveAsync`, one key, never a pattern) |
| `stockly:{shopId}:ai:invoice:{sha256}` | `{promptVersion, model, extraction}`: the validated extraction, the model that made it and the prompt version (`invoice-extraction-v1`); another version is a miss | 7 days | `ProcessInvoiceCommandHandler` (job), right after a valid OpenRouter output and before the database save | never (content-addressed); a prompt change makes old values misses |
| `stockly:{shopId}:invoice:{id}:status` | `{invoiceId, status, updatedAt, clientRequestId, createdAt}` (backend.md §17.1 item 5) | 10 min | scan (after commit), job (after commit), job failure, confirm (Sprint 4), the admin's re-run (AD19, `Processing`, after its commit), read-through `GetInvoiceQueryHandler` (step 2b: a cache miss whose PostgreSQL row is `Processing`) | overwritten on change. Read by `GetInvoiceQueryHandler` (I3): only a `Processing` value answers without PostgreSQL; any other status reads PostgreSQL |
| `stockly:{shopId}:rl:scan:{yyyyMMddHHmm}` | counter (UTC minute) | 120 s | scan endpoint (`RedisScanLimiter`, Lua `INCR` + `EXPIRE` on the first hit) | expires |
| `stockly:{shopId}:quota:scan:{yyyyMMdd}` | counter (Cairo date) | 26 h | scan endpoint (`RedisScanLimiter`, Lua: refuse at the effective quota, else `INCR` + `EXPIRE` on the first hit; release = `DECR` only when > 0) | expires; `DEL` of today's key by the platform admin's quota reset (AD10, `IScanQuotaCounter.ResetAsync`, after the audit row is committed; one key, never a pattern) |
| `stockly:platform:admin:stats` | admin KPIs | 5 min | admin query | expires |

`{yyyyMMdd}` is computed in **Africa/Cairo** time through `IClock`, so the quota resets at local midnight, not UTC.

**The quota itself is not cached (Sprint 5).** The scan reads the effective daily quota from PostgreSQL on every scan (`ShopPlanQueries.GetDailyScanQuotaAsync`: the active subscription's `daily_scan_quota_override`, else its plan's `daily_scan_quota`; one indexed lookup). A quota change by the platform admin (AD8 new plan, AD9 override, AD13 plan edit) therefore applies to the next scan with nothing to invalidate. Only the day's counter lives in Redis; AD10 deletes it. With Redis down, AD10 cannot reset anything (the scan then counts today's invoices in PostgreSQL, which a reset does not change); it still succeeds with the throttled warning (rule 25).

The Hangfire dashboard's one-time codes (contract §11.3) are not in Redis: they live in the API process's memory (one API instance, `docs/features/admin.md` §7).

## 2. Why version keys instead of deleting by pattern

Deleting `stockly:{shopId}:products:*` needs `SCAN`, which is slow and not atomic. Instead every catalog key embeds the current version. A write does one `INCR stockly:{shopId}:ver:catalog`, after commit, and every old key becomes unreachable and expires by TTL. One round-trip, no race with readers.

## 3. Rules (from the master prompt, made concrete)

1. Stock numbers used for a **write** are never read from Redis. Confirm and sale lock the product row with `SELECT ... FOR UPDATE`.
2. Cache invalidation runs **after** the DB transaction commits. Bumping before commit lets a reader re-cache old data.
3. Every key has a TTL except the tiny version counters.
   Redis runs with `maxmemory 256mb` and `maxmemory-policy volatile-lru`, which evicts only keys that have a TTL, so the TTL-less version counters are never evicted (with `allkeys-lru` an evicted counter would restart at 0 and could make stale `v{ver}` entries reachable again).
4. Values are DTOs serialized with System.Text.Json, never EF entities.

## 4. Failure behaviour (Redis down)

| Job | Behaviour | Log |
|---|---|---|
| Read cache | Skip cache, read from Postgres | Warning, throttled to once per minute |
| AI result cache | Treat as miss. The job first reuses an earlier extraction of the same photo from `invoices` (`image_sha256`, backend.md §17.1 item 10), and only then calls OpenRouter (costs money, still correct) | Warning (throttled, shared with the limiter and the status cache) |
| Rate limit | **Fail open**: allow the request | Warning |
| Daily quota | Fall back to `COUNT(*)` on `invoices` created today, where "today" starts at Africa/Cairo midnight from `IClock` (index `invoices (shop_id, created_at DESC)`). Not `ai_usage_records`: it has one row per AI call, including retries and cache hits, so it would over-count scans. | Warning |
| Invoice status | Read from `invoices` table; writes skipped | Warning (throttled) |

Every cache write of the scan and the job happens after the database commit, and a Redis failure there only logs: a scan or a job never fails because of Redis.

Redis connection uses `AbortOnConnectFail = false` and a 250 ms operation timeout, so a dead Redis costs at most 250 ms per request and never crashes startup. Health check `/health/ready` reports Redis as *Degraded* (HTTP 200), not *Unhealthy*; the health check itself times out after 1 s, because it is not on a request path and a cold connection can take longer than 250 ms. Connection strings use `127.0.0.1`, not `localhost`: compose binds IPv4 only and `localhost` can resolve to `::1` first.

## 5. Polling budget

The app polls `GET /api/invoices/{id}` every 2 s for up to 60 s while status is `Processing`. With the status key, one polling scan costs ~30 Redis GETs and zero Postgres queries until the status changes. `GetInvoiceQueryHandler` builds the `Processing` response (no lines, no header) from the cached value alone; once the job has written another status, the next poll reads the full invoice from PostgreSQL. With Redis down every poll reads PostgreSQL (rule 25).
