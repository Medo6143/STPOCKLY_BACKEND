# Load test of the hot endpoints

Sprint 6. Project: `tests/Stockly.LoadTests` (console app, plain `HttpClient` from `IHttpClientFactory`, no load-testing
package). It is **not** part of `dotnet test` (it imports only the root `Directory.Build.props`, `IsTestProject=false`).

## 1. What it measures

| Scenario | Endpoint | How |
|---|---|---|
| P1 product list | `GET /api/products?page=1&pageSize=20` | N users back to back for `--duration` seconds, on a shop with `--products` products |
| S1 create sale | `POST /api/sales` | N users, each sale 1–2 random lines of quantity 1, a fresh `clientRequestId` every time (never a repeat) |
| I1 scan | `POST /api/invoices/scan` | `--scans` uploads shared by N users; every image is unique random bytes behind a JPEG header (the AI cache never answers); 25 scans per shop so the daily quota (30 on the default plan) is never reached |
| I3 poll invoice | `GET /api/invoices/{id}` | N users poll the invoices of I1 for `--duration` seconds while they are still `Processing`, i.e. the **status cache path** (`docs/design/redis.md`); the run prints how many were `Processing` before and after |

Set-up requests (register, create products, register the scan shops) and one warm-up request of each kind are not
measured. Each scenario reports requests, throughput, p50/p95/p99 latency (nearest rank, errors included) and the
error rate (any non-2xx answer or transport error). The exit code is 1 when a scenario's error rate is above 1 %.

## 2. Safety

- The fake AI: `Ai__UseFake=true` makes `FakeOpenRouterHandler` the primary handler of the OpenRouter client, so
  nothing leaves the process and the whole pipeline (job, validation, matching, usage rows, status cache) still runs.
  The Api refuses to start with it unless `ASPNETCORE_ENVIRONMENT=LoadTest` (`src/Stockly.Api/Configuration/FakeAi.cs`,
  tests `FakeAiGuardTests`: Production and Testing fail, LoadTest scans end to end). `Ai__FakeLatencyMs` (0–20000)
  simulates the model's think time so scans stay `Processing` as with the real model.
- The load test writes data (shops, products, sales, invoices). It refuses a non-loopback `--base-url` unless
  `--allow-remote` is given. Never point it at a shared or production deployment.
- Use a **separate database** (and a separate Redis database index) so the developer's data is untouched.

## 3. How to run

1. Infrastructure up: `docker compose up -d`.
2. A separate database, migrated (values from your `.env`; nothing is seeded):
   ```bash
   docker compose exec postgres createdb -U "$POSTGRES_USER" stockly_loadtest
   dotnet ef database update -p src/Stockly.Infrastructure -s src/Stockly.Api \
     --connection "Host=127.0.0.1;Port=$POSTGRES_PORT;Database=stockly_loadtest;Username=$POSTGRES_USER;Password=$POSTGRES_PASSWORD;Maximum Pool Size=100"
   ```
3. Build and start the Api in Release, LoadTest environment, port 5090 (the environment variables of `.env.example`, plus
   these; the `.env` file is **not** read outside Development, so set them in the shell):
   ```bash
   dotnet build src/Stockly.Api -c Release
   export ASPNETCORE_ENVIRONMENT=LoadTest ASPNETCORE_URLS=http://127.0.0.1:5090
   export ConnectionStrings__Postgres="...;Database=stockly_loadtest;Maximum Pool Size=100"
   export ConnectionStrings__Redis="127.0.0.1:$REDIS_PORT,password=...,abortConnect=false,defaultDatabase=5"
   export Storage__Endpoint=... Storage__AccessKey=... Storage__SecretKey=... Storage__Bucket=...
   export Jwt__SigningKey="$(openssl rand -base64 48)" OpenRouter__ApiKey=unused OpenRouter__BaseUrl=http://127.0.0.1:1/
   export Ai__UseFake=true Ai__FakeLatencyMs=8000
   export Scan__RateLimitPerMinute=1000 RateLimiting__Auth__PermitLimit=100000 Hangfire__WorkerCount=2
   export Serilog__MinimumLevel__Default=Warning
   dotnet src/Stockly.Api/bin/Release/net10.0/Stockly.Api.dll
   ```
4. Run the load test:
   ```bash
   dotnet run -c Release --project tests/Stockly.LoadTests -- \
     --base-url http://127.0.0.1:5090 --duration 30 --concurrency 20 --products 50 --scans 100 --image-kb 50
   ```
   `--scenarios p1,s1,i1,i3` runs a subset (I3 needs I1's invoices, so I1 runs whenever I3 is asked).
5. Stop the Api. The jobs left in the queue resume at the next start; drop the database when done.

## 4. Results (2026-10-18)

Machine: developer laptop (Windows 11), Api in Release on .NET 10, PostgreSQL 16, Redis 7 and MinIO in Docker Desktop
(compose), load generator on the same machine. 20 users, 30 s per timed scenario, 100 scans of 50 KB, fake AI latency
8 s, 2 Hangfire workers. Two consecutive runs after the fix in §5:

| Scenario | Requests | Req/s | p50 ms | p95 ms | p99 ms | Error rate |
|---|---|---|---|---|---|---|
| P1 product list | 57380 | 1912.3 | 9.9 | 15.1 | 19.1 | 0.00 % |
| S1 create sale | 3291 | 109.4 | 101.4 | 600.1 | 1477.2 | 0.00 % |
| I1 scan | 100 | 108.6 | 173.5 | 229.2 | 240.6 | 0.00 % |
| I3 poll invoice | 76673 | 2555.7 | 7.2 | 12.0 | 16.2 | 0.00 % |

| Scenario | Requests | Req/s | p50 ms | p95 ms | p99 ms | Error rate |
|---|---|---|---|---|---|---|
| P1 product list | 38587 | 1286.0 | 14.8 | 23.9 | 31.1 | 0.00 % |
| S1 create sale | 761 | 23.8 | 546.8 | 2420.0 | 5005.7 | 0.00 % |
| I1 scan | 100 | 106.2 | 178.1 | 286.2 | 303.4 | 0.00 % |
| I3 poll invoice | 68324 | 2276.9 | 8.0 | 14.6 | 20.0 | 0.00 % |

In both runs all 100 invoices were still `Processing` when I3 started, so I3 measures the Redis status-cache answer.
S1 alone (15 s, same settings): 103 req/s, p50 123 ms, p95 340 ms, p99 2981 ms.

Reading the numbers:
- **P1 and I3** are cache/index reads: p95 well under 25 ms at 1300–2500 req/s.
- **I1** returns in about 0.2 s (upload to MinIO, insert, enqueue); the AI runs later in the job (rule 14).
- **S1** is a write that locks its products in id order (rule 4) and commits a sale, its lines and the stock
  movements. 20 users selling from the **same** 50 products of one shop at full speed is the worst case for row
  locks (a real shop has 1–3 cashiers); the waits are on `SELECT … FOR UPDATE` of the products (seen in
  `pg_stat_activity`), and the variance between runs comes from WAL fsync on Docker Desktop. No error and no deadlock
  in any run.

## 5. Finding fixed: thread-pool starvation on a scan burst

The first run showed I1 p95 **9.0 s**; a burst of 120 scans by 40 users after an idle period gave p95 **22.6 s**,
while the same burst right after was 0.5 s. Cause: Hangfire's enqueue (`BackgroundJobClient`, called by the scan
handler) is synchronous database I/O, so every concurrent scan holds a thread-pool thread; after an idle period the pool
is at its minimum (one thread per core) and grows by about one thread per 0.5 s. Fix: `ThreadPoolMinThreads=64` in
`src/Stockly.Api/Stockly.Api.csproj` (runtimeconfig `System.Threading.ThreadPool.MinThreads`). Same burst after the
fix: p95 **0.86 s**; in the full runs above I1 p95 is 0.23–0.29 s. A later improvement is an asynchronous enqueue
(outbox) instead of the synchronous client.

## 6. Not covered

Real OpenRouter latency and errors (the fake answers after a fixed delay), several Api instances, TLS, and a network
between client and server. Re-run on the production-like server before opening to the first shops.
