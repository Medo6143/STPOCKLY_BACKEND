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
| `stockly:{shopId}:ver:catalog` | integer version | none (tiny) | — | `INCR` on any product/stock write |
| `stockly:{shopId}:products:list:v{ver}:{queryHash}` | paged product DTOs | 10 min | `GetProductsQueryHandler` | version bump |
| `stockly:{shopId}:products:lowstock:v{ver}` | low-stock DTOs | 5 min | `GetLowStockQueryHandler` | version bump |
| `stockly:{shopId}:dashboard:summary:v{ver}` | dashboard DTO | 2 min | `GetDashboardQueryHandler` | version bump |
| `stockly:{shopId}:suppliers:list` | supplier DTOs | 30 min | `GetSuppliersQueryHandler` | `DEL` on supplier write |
| `stockly:{shopId}:ai:invoice:{sha256}` | validated extraction JSON | 7 days | `ProcessInvoiceJob` | never (content-addressed) |
| `stockly:{shopId}:invoice:{id}:status` | `{status, updatedAt}` | 10 min | `ProcessInvoiceJob`, confirm | overwritten on change |
| `stockly:{shopId}:rl:scan:{yyyyMMddHHmm}` | counter | 120 s | scan endpoint | expires |
| `stockly:{shopId}:quota:scan:{yyyyMMdd}` | counter | 26 h | scan endpoint | expires |
| `stockly:platform:admin:stats` | admin KPIs | 5 min | admin query | expires |

`{yyyyMMdd}` is computed in **Africa/Cairo** time through `IClock`, so the quota resets at local midnight, not UTC.

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
| AI result cache | Treat as miss, call OpenRouter (costs money, still correct) | Warning |
| Rate limit | **Fail open**: allow the request | Warning |
| Daily quota | Fall back to `COUNT(*)` on `invoices` created today, where "today" starts at Africa/Cairo midnight from `IClock` (index `invoices (shop_id, created_at DESC)`). Not `ai_usage_records`: it has one row per AI call, including retries and cache hits, so it would over-count scans. | Warning |
| Invoice status | Read from `invoices` table | none |

Redis connection uses `AbortOnConnectFail = false` and a 250 ms operation timeout, so a dead Redis costs at most 250 ms per request and never crashes startup. Health check `/health/ready` reports Redis as *Degraded* (HTTP 200), not *Unhealthy*; the health check itself times out after 1 s, because it is not on a request path and a cold connection can take longer than 250 ms. Connection strings use `127.0.0.1`, not `localhost`: compose binds IPv4 only and `localhost` can resolve to `::1` first.

## 5. Polling budget

The app polls `GET /api/invoices/{id}` every 2 s for up to 60 s while status is `Processing`. With the status key, one polling scan costs ~30 Redis GETs and zero Postgres queries until the status changes.
