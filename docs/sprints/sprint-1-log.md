# Sprint 1 log

Running record of Sprint 1: each step's plan, decisions, deviations and results. Newest step at the bottom.

## Run rules (from step 1.4a on)

After 1.4a was approved, steps 1.4a → 1.7 run without approval stops. Each step: plan written here, tests first, build and tests green with zero warnings, docs, AGENTS.md §11 self-review, small Conventional Commits on `feature/<name>`, merge into `main` with `--no-ff`, next branch from `main`. Never push.

Stop conditions. The message defining "the four stop conditions" did not reach the agent, so these conservative conditions, taken from the session prompt and AGENTS.md, are used until the product owner confirms or replaces them:

1. Sources disagree or are silent on a design decision that is hard to reverse.
2. Build or tests cannot be made green with zero warnings without skipping, deleting or weakening a test.
3. A destructive or out-of-repo action would be needed (deleting user data, touching other projects' services or containers, pushing, installing system software).
4. An AGENTS.md §5 rule or a security rule (secrets, tenant isolation) would have to be broken.

## Steps 1.1–1.3 (approved with stops)

| Step | Branch | Result |
|---|---|---|
| 1.1 Repository skeleton | `feature/repo-skeleton` | Solution, central packages, architecture tests (csproj allowlist + NetArchTest + controller signatures) |
| 1.2 Docker Compose | `feature/docker-compose` | Postgres, Redis, MinIO (pgsty, digest-pinned), minio-init with least-privilege API user, pgAdmin (dev) |
| 1.3 Domain core | `feature/domain-core` | Entity/Result/Error/Precision, ArabicTextNormalizer, Product/ProductUnit/StockMovement; 178 tests, 99.1% line coverage |

## Step 1.4a: Persistence

**Plan (approved):**
- Tables now: `shops`, `products`, `product_units`, `stock_movements`. `users`, `refresh_tokens`, `shop_members`, `plans`, `shop_subscriptions` in 1.5 (shop_members needs users). `products.barcode`/`category` and their index in 1.6.
- Minimal `Shop` Domain entity (not `ITenantEntity`: it is the tenant).
- Deferred FKs as plain columns: `stock_movements.created_by_user_id` → users (1.5), `invoice_id`/`invoice_item_id` (Sprint 2), `sale_id`/`sale_item_id` (Sprint 4).
- FKs now: `*.shop_id` → shops (Restrict), `product_units.product_id` → products (Cascade, needed for unit removal), `stock_movements.product_id` → products (Restrict).
- `ICurrentTenant`, `IClock` in Application; `SystemClock`, `StocklyDbContext`, configurations, `TenantStampInterceptor`, `AuditTimestampsInterceptor`, `AddPersistence`, design-time factory in Infrastructure.
- Tenant filter applied by looping over every `ITenantEntity`; fails closed (no tenant → no rows). Writes without a tenant throw.
- `xmin` (shadow, `IsRowVersion`) on products; `updated_at` shadow property set by the interceptor.
- New test project `Stockly.Infrastructure.Tests` (Testcontainers, compose's pinned Postgres image, guarded by a test).
- Architecture tests: query filter on every tenant entity in the model; `IgnoreQueryFilters` only under `Application/Features/Admin`.

**Decisions recorded for later steps:**
- 1.5: scoped `ITenantScope` (Application) with `BeginForNewShop(Guid shopId)`, read by `ICurrentTenant` during the register request. Architecture test (source scan): only `Application/Features/Auth/Register` may use it.
- Timestamps are UTC: Npgsql rejects `timestamptz` values with a non-zero offset. Documented, tested (`SystemClock.UtcNow.Offset == TimeSpan.Zero`), no converter.

**Result (step 1.4a):**
- Migration `InitialCatalog`: 4 tables, `pg_trgm`, all ERD indexes and checks for them, plus EF's FK support index `ix_stock_movements_product_id` (documented in erd.md §3.7).
- Tests: Domain 185, Infrastructure 36 (Testcontainers), Architecture 18, Api 1. Coverage: Domain 99.1% line / 99.5% branch, Infrastructure 89.9%.
- Deliberate mutations caught: tenant filter widened (2 failures), interceptor accepting another shop (2), ShopId change allowed (1).
- Architecture failure demo: an `IgnoreQueryFilters` call in Infrastructure and an unfiltered `StockMovement` both fail.
- Smoke check: `dotnet ef database update` against compose Postgres applies `InitialCatalog`; second run is a no-op.

**Findings fixed during 1.4a:**
- EF does not map get-only properties by convention: every get-only Domain property is mapped explicitly; `StockMovement` has a private parameterless constructor for EF.
- EF history table name differed between design time (`__ef_migrations_history`) and runtime (`__EFMigrationsHistory`) with the snake_case plugin. Pinned explicitly; a test guards it.
- Generated migration files are marked `generated_code = true` in `.editorconfig` instead of being hand-edited.

## Interrupts received during 1.4a (order of work)
1. Finish and merge 1.4a.
2. Step `feature/repo-split`: this repo becomes `stockly-backend`, backend at the repo root, brand and mobile rules move out, ADR-0011 polyrepo.
3. Step `feature/upgrade-net10`: .NET 10 LTS (.NET 8 support ends 2026-11-10), `Guid.CreateVersion7` replaces UUIDNext (ADR-0010 supersedes ADR-0009).
4. Continue with 1.4b (now including OpenAPI spec at `docs/api/openapi.json` with an up-to-date test), 1.5, 1.6, 1.7.

## Step repo-split: this repo becomes `stockly-backend`

**Plan:**
- `git mv` the contents of `backend/` to the repo root (`Stockly.sln`, `Directory.*.props`, `src/`, `tests/`); `backend/` disappears. History is kept (checked with `git log --follow`).
- Copy `brand/` to `D:\FLUTTER\stockly-mobile-assets\brand` (outside the repo), then `git rm -r brand`.
- Flutter/Dart sections leave `.gitignore` and `.editorconfig`; editorconfig globs lose the `backend/` prefix.
- AGENTS.md: mobile-only rules (34–39), Dart conventions, mobile tests and mobile commands move to `D:\FLUTTER\stockly-mobile-assets\AGENTS.mobile.md`. Rule numbers 34–39 stay reserved with a pointer, so references such as "rule 42" in code comments stay valid. A short section says the mobile app lives in `stockly-mobile` and consumes the API through OpenAPI.
- `structure.md` §3 (mobile tree) moves to `D:\FLUTTER\stockly-mobile-assets\docs\structure.mobile.md`; the backend tree is now the repo root.
- `Stockly.Admin` (Sprint 5) stays here: it references `Stockly.Contracts`.
- ADR-0011 "Polyrepo: stockly-backend (API + Admin) and stockly-mobile".
- Paths updated in README, AGENTS.md, structure.md, roadmap, products.md, this log and the execution prompt.
- Acceptance: build with 0 warnings and all tests green from the root; `docker compose up -d` healthy; `dotnet ef` from the root; `git log --follow` on a moved file shows its history.

**Result (repo-split):**
- Pure renames (`git mv`): 93 files, 0 content changes in moved files. `git log --follow src/Stockly.Domain/Products/Product.cs` shows its history from before the move.
- `D:\FLUTTER\stockly-mobile-assets\` now holds `brand/` (byte-identical copy, checked with `diff -r`), `AGENTS.mobile.md` and `docs/structure.mobile.md`. It is not a git repository yet.
- From the root: build 0 warnings / 0 errors; 240 tests green; `docker compose up -d` healthy with minio-init exited 0; `dotnet ef migrations list` and `database update` work.
- ADR-0011 added. Paths updated in README, AGENTS.md, structure.md, roadmap, products.md and the execution prompt.

## Step upgrade-net10: .NET 8 → .NET 10 LTS

**Why:** .NET 8 support ends 2026-11-10; .NET 10 LTS is supported until November 2028. Done while the codebase is small.

**Plan:**
- `global.json` → SDK `10.0.100`, `rollForward: latestFeature` (installed: 10.0.401, runtime 10.0.12).
- `Directory.Build.props` → `net10.0`; `LangVersion` removed (C# 14 is the default for net10.0).
- `Directory.Packages.props` → Microsoft.* / EF Core / Npgsql / EFCore.NamingConventions / Mvc.Testing / Design to their latest 10.x; `dotnet-ef` local tool 10.0.12. MediatR 12.x and FluentAssertions 7.x stay pinned (license). Test SDK 18.x. Other packages checked for net10.0 support.
- `Entity` uses `Guid.CreateVersion7()`; UUIDNext removed; Domain references no packages. ADR-0010 supersedes ADR-0009. Architecture allowlist for Domain becomes empty.
- `Guid.CreateVersion7()` has no counter inside a millisecond (UUIDNext had one). The id test keeps "v7 and time-ordered" by asserting what v7 guarantees: embedded timestamps never decrease, and ids from different milliseconds sort in creation order. Recorded in ADR-0010.
- Migration `InitialCatalog` verified under EF Core 10 (model diff and script diff).
- Docs: AGENTS.md stack, README, structure.md, roadmap, execution prompt.
- Acceptance: 0 warnings, all tests green with and without coverage, architecture failure demo still fails, compose + migration smoke check pass.

**Result (upgrade-net10):**
- SDK 10.0.401 / runtime 10.0.12. All packages on their latest 10.x (EF Core, ASP.NET, Mvc.Testing 10.0.12; Npgsql EF 10.0.3; EFCore.NamingConventions 10.0.1; Serilog.AspNetCore 10.0.0); HealthChecks 9.0.0 (latest, supports net10.0); Testcontainers 4.16.0 and UUIDNext-free Domain; test SDK 18.10.1; coverlet.collector 6.0.4 works on net10.0. MediatR 12.5.0 and FluentAssertions 7.2.2 unchanged.
- Only code change forced by the upgrade: EF Core 10 named query filters (`GetQueryFilter()` → `GetDeclaredQueryFilters()` in `TenantFilterTests`).
- Migration under EF Core 10: `has-pending-model-changes` → "No changes have been made to the model since the last migration". Script diff vs EF 8: one blank line and the product version written to the history row (`8.0.31` → `10.0.12`). Migration kept as is.
- `Guid.CreateVersion7` replaces UUIDNext; ADR-0010 supersedes ADR-0009. The old strictly-ascending id assertion fails in 1000 of 1000 runs with `Guid.CreateVersion7`, so the test now asserts the v7 guarantees (version, variant, non-decreasing timestamps, order across milliseconds).

## Step 1.4b: API cross-cutting

**Plan:**
- **Configuration loading (decision).** Production reads real environment variables (`ConnectionStrings__Postgres` → `ConnectionStrings:Postgres`). In Development only, the Api also loads the repo-root `.env` through a small parser in `Api/Configuration` (no package), inserted *below* environment variables: precedence is appsettings < `.env` < environment variables < command line. Required settings are checked at startup with a clear message naming the variable, never its value. `.env` stays the single source for local secrets (AGENTS.md rule 11); user-secrets are not used.
- **ICurrentTenant** implemented in `Api/Identity` from the `shop_id` claim (ADR-0002); missing or malformed claim → `null` (fail closed). The claim name lives in `Application/Abstractions/Identity/StocklyClaimTypes` so the token service (1.5) uses the same constant.
- **ProblemDetails** (RFC 7807): `AddProblemDetails` with `traceId`, `correlationId` and `instance` on every problem; `GlobalExceptionHandler` (`IExceptionHandler`) logs and returns 500 without exception details; `UseStatusCodePages` gives empty 4xx responses a problem body.
- **Correlation id** middleware: `X-Correlation-Id` accepted if 1–64 chars of `[A-Za-z0-9._-]`, otherwise a new id; echoed on the response, pushed to Serilog `LogContext`.
- **Serilog** from configuration (`appsettings*.json`): compact JSON to console outside Development, readable text in Development; request logging without query strings or headers; EF/ASP.NET noise at Warning.
- **Health checks**: `/health/live` (no dependencies), `/health/ready` with PostgreSQL (Unhealthy → 503) and Redis (failure reported as **Degraded**, HTTP 200, docs/design/redis.md §4). JSON body lists status and duration per check, never exception messages.
- **OpenAPI** (ADR-0011): `Microsoft.AspNetCore.OpenApi` document `v1`, generated at build time by `Microsoft.Extensions.ApiDescription.Server` into `docs/api/openapi.json`. Build-time generation is skipped when `CI=true`, so in CI the file on disk is the committed one, and `OpenApiContractTests` fails if the runtime document differs from it. Locally every build refreshes the file; commit it with the change. Runtime endpoint `/openapi/v1.json` is mapped outside Production.
- **Tests** (`Stockly.Api.IntegrationTests`, Testcontainers Postgres + Redis using the images parsed from `docker-compose.yml`): health live/ready (healthy, Redis down → Degraded 200, Postgres down → 503); ProblemDetails for 404 and for an unhandled exception (no leak); correlation id echo/replace/generate; missing connection string fails startup with a clear message; `.env` parser and precedence; tenant from claims; OpenAPI contract.
- `ResultExtensions` (Result → HTTP) waits for the first controller in 1.5 (no dead code).

**Result (step 1.4b):**
- Tests: Api 36 (Testcontainers Postgres + Redis from the compose images), Domain 186, Infrastructure 36, Architecture 18: 276 green, 0 warnings.
- OpenAPI: build writes `docs/api/openapi.json` (OpenAPI 3.1.1, no paths yet). Demo: with `CI=true` a stale committed spec fails `OpenApiContractTests`; a local `--no-incremental` build rewrites it and the test passes. Generation is incremental on the Api assembly, so a hand-edited file is only rewritten by the next Api change or a `--no-incremental` build (documented).
- Real run (`dotnet run`, Development, compose): `.env` loaded; `/health/ready` Healthy; 404 returns `application/problem+json` with `traceId`, `correlationId`, `instance`; `X-Correlation-Id` echoed; logs carry the correlation id and contain no secrets (scanned).

**Finding fixed during 1.4b:** `.env.example` used `localhost` in connection strings. On Windows StackExchange.Redis tried `::1` first, compose binds `127.0.0.1` only, so Redis was permanently Degraded. All connection strings now use `127.0.0.1` (`.env.example`, README, redis.md).

### Step 0 (orchestration): finish 1.4b

- First commit built in a fresh worktree on D: (`D:\FLUTTER\_wt`), outside the repo, no `.env`: `dotnet restore` + `dotnet build -warnaserror` → 0 warnings, 0 errors. No error lines to report.
- All six 1.4b commits build the same way (0 warnings each, no `.env`).
- Cause (a) ruled out: without `.env`, without any `ConnectionStrings__*` variable and without `CI=true`, the build runs OpenAPI generation (placeholders, host built but never started, no `ValidateOnStart`) and reproduces the committed `docs/api/openapi.json` byte-for-byte.
- Cause (b) ruled out: every commit builds on its own.
- **Root cause (c): environment.** The earlier per-commit worktrees were in the agent scratchpad on drive C:, which is full (0.02–0.05 GB free); restore/build output failed to write. Worktrees now go to `D:\FLUTTER\_wt` and temp to `D:\FLUTTER\_tmp`.
- Clean clone of `feature/api-cross-cutting`, no `.env`: build 0 warnings; Domain 186, Architecture 18 and the non-container Api tests 17 pass. The 55 container tests (Infrastructure 36, Api 19) passed on the same `src/` before C: filled up; Docker Desktop does not respond while C: is full, so their re-run is pending (blocker reported to the product owner).

## Phase 0 (orchestration, see docs/agents/ORCHESTRATION.md)

- Step 0 done (above); 1.4b merged (`9cf0485`).
- `docs/agents/ORCHESTRATION.md`, `CLAUDE.md` (`@AGENTS.md`), `docs/agents/briefs/_TEMPLATE.md` committed and merged (`00e3843`).
- `docs/api/contract.md` v1 written (27 endpoints A1–D2, conventions, error catalogue, 17 decisions for review).
- **Decision:** `docs/api/openapi.json` is generated by a test (`OpenApiDocumentTests`), not by the build. Removed `Microsoft.Extensions.ApiDescription.Server` and the build-time placeholder logic, so the build never starts the host; startup now always requires the connection strings. On mismatch the test rewrites the file and fails once.
- `ContractConformanceTests` (no containers): the index parses (27+ rows, unique), every spec operation is an `implemented` row with identical status codes, every `implemented` row is in the spec. Failure demo: a row wrongly marked implemented and an undocumented `DELETE /api/secret-admin` both fail with readable messages.
- **Blocker:** drive C: is full (0.05 GB free); Docker Desktop does not respond, so container tests (Infrastructure 36, Api 19) and compose cannot run. Reported at checkpoint 1.

## Lane DOCS: Sprint 2 backend design

- Agent: general-purpose worker (background). Brief: `docs/agents/briefs/docs-sprint2-backend-design.md` (contract v1.1 §1 and §6 copied in).
- Report: "AGENTS.md read: yes", contract deviations NONE, changed only `docs/design/backend.md` (new, 17 sections) and the allowed parts of `docs/erd.md`.
- Orchestrator verification: `git status` matches the allowed scope; ERD diff read in full; spot-checked §6 resilience (retry only 429/5xx, 30 s per attempt, max 2 retries, fallback in the extractor: rule 17), §10 #13 (never both / confirmed never neither enforced by CHECKs with `confirmed_at` and `created_product_id`; `product_id` stays NULL after confirm so "never both" holds forever), §11 confirm (lock order invoice → products `ORDER BY id FOR UPDATE`, idempotent, cache bump after commit: rules 1, 3, 4, 23, 24). Result: accepted.
- Notable design decisions (for the product owner before Sprint 2): two supporting columns on `invoice_items` (`confirmed_at`, `created_product_id`); unreadable AI lines are counted, not saved; timeouts go to the fallback model without retry; tenant for jobs via a scoped `JobTenantContext`.
- 15 open questions in `docs/design/backend.md` §17 (contract v1.2 candidates: `newProduct` on `InvoiceItemDto`, 413/429 `ErrorType`s; others: rate limit value, line_total rounding, Hangfire DDL vs rule 31, image key extension, model ids, ...). They block Sprint 2 implementation, not Sprint 1.

## Lane BACKEND: step 1.5 auth

- Agent: general-purpose worker (background). Brief: `docs/agents/briefs/backend-1.5-auth.md` (contract v1.2 §1, A1–A5, §3, §4, codes; product-owner decisions: membership access by user id only, default plan 30 scans/day, token families).
- Report: "AGENTS.md read: yes", contract deviations NONE.
- Orchestrator verification:
  - Build 0 warnings; tests Domain 221, Application 71, Architecture 23, Infrastructure 82, Api 139 (536, all green), on the branch and again on merged `main`. `has-pending-model-changes`: none.
  - Migration `Identity` SQL read: every erd §3.1 index (unique normalized_email, token_hash, partial active indexes, one Owner per shop, one active subscription per shop, plans code) plus FK support indexes; deferred FK `stock_movements.created_by_user_id → users` is real; default plan seeded (`standard`, 30, 3, 0.00).
  - Code read: refresh locks the token row (`FOR UPDATE`), reuse revokes only that family (`ExecuteUpdate` on `family_id`), commit-on-failure only for refresh (`ICommitOnFailure`); tenant = registration scope first, then `shop_id` claim, fail closed; `shop_members` only through `ShopMembershipRepository` (every read takes a user id; 4 architecture tests); no `IgnoreQueryFilters`; no secrets logged; `.env.example` adds `Jwt__Issuer`/`Jwt__Audience` only.
  - Required test present: `Reuse_OnDeviceA_RevokesOnlyDeviceAFamily_DeviceBStillRefreshes`.
- Commits: `97d804f` feat (auth), `6b3c6cb` test (architecture), `026fe76` docs; merge `da63d39` (contract change-log conflict resolved in favour of `main`'s v1.3 line; A1–A5 `implemented`).
- Follow-up by the orchestrator: `refresh_tokens.family_id` added to the erd §2 diagram.
- Worker decisions accepted: shop type as a string in the request (so an unknown type gives `Shop.UnknownType`); expiry timestamps as UTC `DateTime` serialized with `Z`; invariant formatting culture with ar/en UI culture; existing tests updated, none weakened (500 test now sends `Accept-Language: en`, a new test checks the Arabic title).
- Open (known limitation, docs/features/auth.md): no login lockout or rate limiting yet.
- Tool note: the `dotnet-ef` resolver cache pointed at the old C: NuGet path after the cache move; the stale cache entry was deleted and `dotnet tool restore` re-run.

## Lane BACKEND: step 1.5b auth hardening

- Brief: `docs/agents/briefs/backend-1.5b-auth-hardening.md`; contract v1.4 written first on the branch (A1 + 429, A2 + 423/429), conformance red until done.
- Report: "AGENTS.md read: yes", contract deviations NONE. The worker once ran `git checkout -- <file>` to undo its own edit (file identical to HEAD); harmless, and the brief template now forbids any git command that changes the working tree or history.
- Orchestrator verification: build 0 warnings; tests Domain 222, Application 73, Architecture 23, Infrastructure 87, Api 165 (570, green); no pending model changes; scope = src, tests, openapi.json, auth.md, README, .env.example.
- Identity clock question (product owner): checked the Identity 10.0.12 assemblies, including the shared-framework copy: `Microsoft.Extensions.Identity.Core` (UserManager) has no `TimeProvider` reference and reads `DateTimeOffset.UtcNow` for lockout; only `Microsoft.AspNetCore.Identity` (SignInManager, security stamp) uses `TimeProvider`. So a DI `TimeProvider` cannot drive lockout; `StocklyUserManager` overrides `IsLockedOutAsync` and `AccessFailedAsync` (same logic as stock, time from `ClockTimeProvider` over `IClock`). Documented in `docs/features/auth.md`; covered by a Testcontainers test with a fake clock (stored `lockout_end`, boundary at 15 min ± 1 s) and the Api lockout tests.
- Other decisions accepted: login is `ICommitOnFailure` so failed-attempt counts survive the failed result; `Retry-After` on 429 is the window length (upper bound) from the lease metadata; rate-limit settings validated at startup.

## Lane BACKEND: step 1.6 products

- Brief: `docs/agents/briefs/backend-1.6-products.md` (contract v1.4; P1–P11 flipped to implemented first; low-stock cache key made page-aware: `products:lowstock:v{ver}:{queryHash}`, redis.md updated).
- First report: 860 tests green, deviations NONE. Orchestrator verification: build 0 warnings, 860 green, no pending model changes, scope ok; read `CatalogCacheInvalidationBehavior` (runs outside `TransactionBehavior`: bump only after commit and only on success), `StockService` (`SELECT 1 ... FOR UPDATE`, refuses to run outside a transaction), Redis-down tests (degrade, fail fast, no connection string logged). Migration SQL: `ALTER TABLE products ADD barcode text; ADD category text; CREATE UNIQUE INDEX ux_products_shop_id_barcode ON products (shop_id, barcode) WHERE barcode IS NOT NULL AND NOT is_archived`.
- Sent back once: a concurrent duplicate barcode returned 500 instead of the contract's 409. Fix (with the product owner's extra requirements): only SqlState 23505 on `ux_products_shop_id_barcode` / `ux_product_units_shop_id_barcode` maps to `409 Product.DuplicateBarcode` / `409 ProductUnit.DuplicateBarcode` (contract codes kept; no new code), everything else stays 500; deterministic race tests plus truly parallel `Task.WhenAll` tests for product and unit barcodes; a test that another unique violation stays 500.
- Final verification (twice): build 0 warnings; Domain 260, Application 232, Architecture 23, Infrastructure 127, Api 230 = 872 green; no pending model changes.
- Worker decisions accepted: two-stage decimal parsing (shape → `Validation.Failed`, then `Precision.*`; places counted on the string; `[0-9]` not `\d`); global `JsonNumberHandling.Strict`; separate `IProductQueries` read port; Redis `BacklogPolicy.FailFast`; cache skipped when the version cannot be read; `IUnitOfWork.TrySaveChangesAsync` for the barcode conflict.
- Open (backlog): no maximum length for product/unit names in the contract; `lowStock=true&includeArchived=true` is not served by the partial index (documented).

## Step 1.7: Sprint 1 review

**Setup (clean clone, no `.env`):** `git clone` of `feature/sprint-1-review` into `D:\FLUTTER\_wteview`; `.env` copied from `.env.example`, only host ports changed (5435/6381/9010/9011/5051, because this machine already uses 5432/6379); compose project `stockly-review` (separate volumes, the dev stack untouched): postgres, redis, minio healthy, minio-init exited 0. `dotnet build -warnaserror`: 0 warnings. `dotnet ef database update`: applied `InitialCatalog`, `Identity`, `ProductsCatalog`; default plan seeded. API: `dotnet run` (Development, reads the clone's `.env`) on `http://127.0.0.1:5080`; `/health/ready` Healthy.

**Flow:** `docs/review/sprint-1-review.http` (dev placeholders only), executed top to bottom against a fresh database in 16 s:

```
 R1 POST /api/auth/register -> 201 {"shop.role": "Owner"}
 R2 POST /api/auth/register -> 201 {"shop.role": "Owner"}
 L1 POST /api/auth/login -> 200 {"shop.role": "Owner"}
  3 GET /api/auth/me -> 200 {"shop.role": "Owner"}
  4 POST /api/products -> 201 {"quantity": "24.000", "isLowStock": false}
  5 POST /api/products/01a13200-cd23-7917-8121-de209bb18e02/units -> 201 {}
  6 POST /api/products/01a13200-cd23-7917-8121-de209bb18e02/adjustments -> 201 {"balanceAfter": "18.000", "quantityChange": "-6.000"}
  7 POST /api/products/01a13200-cd23-7917-8121-de209bb18e02/stock-counts -> 200 {"quantity": "17.000", "changed": true, "movement.reason": "StockCount"}
  8 POST /api/products/01a13200-cd23-7917-8121-de209bb18e02/stock-counts -> 200 {"quantity": "17.000", "changed": false}
  9 GET /api/products?lowStock=true&page=1&pageSize=20 -> 200 {"totalCount": 1, "items": [{"name": "بنادول اكسترا", "quantity": "17.000", "isLowStock": true}]}
 10 GET /api/products/01a13200-cd23-7917-8121-de209bb18e02/movements?page=1&pageSize=20 -> 200 {"totalCount": 3, "items": [{"reason": "StockCount", "quantityChange": "-1.000", "balanceAfter": "17.000", "note": null}, {"reason": "ManualAdjustment", "quantityChange": "-6.000", "balanceAfter": "18.000", "note": "Expired, thrown away"}, {"reason": "StockCount", "quantityChange": "24.000", "balanceAfter": "24.000", "note": "Opening stock"}]}
 11 POST /api/products/01a13200-cd23-7917-8121-de209bb18e02/adjustments -> 400 {"code": "Precision.TooManyDecimals", "detail": "The value has too many decimal places (money 2, quantity 3, factor 6).", "status": 400}
 L2 POST /api/auth/login -> 401 {"code": "Auth.InvalidCredentials", "detail": "البريد الإلكتروني أو كلمة المرور غير صحيحة.", "status": 401}
 L3 POST /api/auth/login -> 401 {"code": "Auth.InvalidCredentials", "detail": "البريد الإلكتروني أو كلمة المرور غير صحيحة.", "status": 401}
 L4 POST /api/auth/login -> 401 {"code": "Auth.InvalidCredentials", "detail": "البريد الإلكتروني أو كلمة المرور غير صحيحة.", "status": 401}
 L5 POST /api/auth/login -> 401 {"code": "Auth.InvalidCredentials", "detail": "البريد الإلكتروني أو كلمة المرور غير صحيحة.", "status": 401}
 L6 POST /api/auth/login -> 401 {"code": "Auth.InvalidCredentials", "detail": "البريد الإلكتروني أو كلمة المرور غير صحيحة.", "status": 401}
 L7 POST /api/auth/login -> 423 Retry-After=900 {"code": "Auth.LockedOut", "detail": "محاولات دخول فاشلة كثيرة. تم قفل حسابك لمدة 15 دقيقة. حاول مرة أخرى لاحقاً.", "status": 423}
 L8 POST /api/auth/login -> 200 {"shop.role": "Owner"}
 L9 POST /api/auth/login -> 200 {"shop.role": "Owner"}
L10 POST /api/auth/login -> 200 {"shop.role": "Owner"}
L11 POST /api/auth/login -> 429 Retry-After=60 {"code": "Auth.TooManyRequests", "detail": "Too many requests. Please wait a moment and try again.", "status": 429}
  H GET /health/ready -> 200 {"status": "Healthy", "checks": [["postgres", "Healthy"], ["redis", "Healthy"]]}
```

All results match the expected values written in the file: opening stock as a `StockCount` movement with note "Opening stock"; unit conversion (2 boxes × 3 = 6 base units); no zero movement on an unchanged count; low-stock list; movement history newest first; precision rejected, not rounded; lockout with the correct password (423, `Retry-After` 900, Arabic message); account B unaffected; the 11th login request in the minute is rate limited (429, `Retry-After` 60).

### Definition of Done: Sprint 1

| Item | Code follows AGENTS.md (§11) | Tests | Docs | Migration reviewed | Clean `docker compose up -d` | Done |
|---|---|---|---|---|---|---|
| Solution, 5 + 5 projects, `Directory.Build.props`, central packages (.NET 10) | ✅ | ✅ architecture 23 | ✅ structure.md, ADR-0009/0010/0011 | n/a | ✅ | ✅ |
| Architecture tests (layers, tenant filter, IgnoreQueryFilters, tenant scope, shop members) | ✅ | ✅ | ✅ | n/a | n/a | ✅ |
| docker-compose (postgres, redis, minio, minio-init, pgadmin) + `.env.example`, health checks | ✅ | ✅ (1.2 verification) | ✅ README, structure.md, ADR-0008 | n/a | ✅ | ✅ |
| Domain: Product, ProductUnit, StockMovement, Result/Error, ArabicTextNormalizer | ✅ | ✅ Domain 260 (99% lines) | ✅ products.md | n/a | n/a | ✅ |
| DbContext, tenant filter + interceptors, first migration with all indexes | ✅ | ✅ Infrastructure 127 | ✅ erd.md §3.7 | ✅ | ✅ | ✅ |
| Auth: register, login, refresh rotation (token families), logout, me; lockout; rate limit | ✅ | ✅ Application + Api | ✅ auth.md, ADR-0002 update | ✅ `Identity` | ✅ (review flow) | ✅ |
| Products: create (opening stock), update, archive, list (paged, search, low stock), get, units CRUD, adjust, stock count, movements | ✅ | ✅ Api 230 | ✅ products.md, erd.md | ✅ `ProductsCatalog` | ✅ (review flow) | ✅ |
| Redis `ICacheService` + version keys, Redis-down fallback | ✅ | ✅ Testcontainers Redis | ✅ redis.md | n/a | ✅ | ✅ |
| ProblemDetails (+ `code`), Serilog, correlation id, `/health/live`, `/health/ready` | ✅ | ✅ | ✅ README | n/a | ✅ | ✅ |
| OpenAPI spec generated in a test + contract conformance (contract v1.4) | ✅ | ✅ | ✅ contract.md | n/a | n/a | ✅ |
| Docs: `features/auth.md`, `features/products.md` | ✅ | n/a | ✅ | n/a | n/a | ✅ |

Totals on `main` after 1.6: Domain 260, Application 232, Architecture 23, Infrastructure 127, Api 230 = **872 tests**, 0 warnings. Coverage: Domain 99.0%, Application 97.5% (target ≥ 80%).

### Known gaps after Sprint 1 (backend)
- CI (GitHub Actions) not set up yet (roadmap: Sprint 6).
- Rate limiting uses the connection IP; forwarded headers must be configured behind a reverse proxy at deployment (contract decision 19).
- Lockout tells a caller who failed 5 times that the account exists (accepted trade-off, contract decision 18).
- No maximum length for product and unit names in the contract; `lowStock=true&includeArchived=true` not served by the partial index.
- No shop switching, no member management, no `PATCH /api/auth/me` (backlog).
- `ProductUnit.InUse` (P8) arrives with invoice/sale lines (Sprint 2+).
- Docker's disk image stays on C: (product-owner decision); C: free space is monitored.

---

# Sprint 2: AI scan pipeline

## Orchestrator decisions (2026-10-17)

- The product owner asked for the remaining sprints to run without pauses. The 15 open questions of `docs/design/backend.md` §17 were resolved by accepting each proposal (§17.1).
- Branch names no longer contain "sprint" (product owner request): `feature/sprint-2-scan` → `feature/invoice-scan`, `feature/sprint-1-review` → `feature/catalog-review`, `docs/sprint2-backend-design` → `docs/backend-design`.
- Contract v1.5 (admin API, AD1–AD20) merged ahead of Sprint 5 so its briefs could be written while workers run.

## Integration check on the emulator (2026-10-17)

- API from `main` on `http://127.0.0.1:5000` (Development, dev stack), app from mobile `main` (debug, real backend, `http://10.0.2.2:5000`, debug-only cleartext config) on QA_Pixel_7.
- From the app: register → `201`, product list → `200`, create product with opening stock 24 → `201`, product detail shows quantity, prices, low-stock level and barcode from the database. English UI.

## Step 2a: scan pipeline

- Brief: `docs/agents/briefs/backend-2a-scan-pipeline.md`. Branch `feature/invoice-scan`. Worker: general-purpose agent (background).
- Report: "AGENTS.md read: yes". One blocker: `ContractConformanceTests` failed because I1 was live but marked `planned`; the orchestrator marked I1 `implemented` (shape and statuses already matched).
- Orchestrator verification: `dotnet build -warnaserror` 0 warnings; `dotnet test` 1,133 passed (Domain 303, Application 346, Architecture 23, Infrastructure 209, Api integration 252). Read the I1 controller against contract I1; grep for `IgnoreQueryFilters`, `DateTime.Now/UtcNow`, `new HttpClient`, `TODO`, `NotImplementedException` in `src`: none.
- Migration `20261017180927_InvoiceScan`: `hangfire` schema; `suppliers`, `product_aliases`, `invoices`, `invoice_items`, `ai_usage_records` with §12.2 indexes and CHECKs; real FKs from `stock_movements` to invoices and invoice items.
- Decisions accepted: `Newtonsoft.Json` 13.0.4 pinned (security fix of a Hangfire transitive dependency, not a new library); `Testcontainers.Minio` 4.15.0 (4.16.0 does not exist); Hangfire without static global configuration (lazy storage, server host waits for Postgres); `ErrorType.PayloadTooLarge` = 9 and `Retry-After` through the existing `Error.RetryAfter`; `[ScanUploadLimit]` resource filter for the 413; logging of scan and job in Infrastructure; lines saved unmatched until 2b; `.env.example` model overrides commented out (an empty value fails start-up).
- Process note: the worker's output was committed as one `wip:` commit (`bbf2f13`) so `main` (contract v1.5) could be merged in before the contract fix; it is not split per concern.

## Step 2b: matching and review

- Brief: `docs/agents/briefs/backend-2b-matching-review.md`. Branch `feature/invoice-review`. Worker: general-purpose agent (background); resumed once after a network error, no work lost.
- Report: "AGENTS.md read: yes", contract deviations NONE. Expected conformance failure only (I2–I4 live); the orchestrator marked I2, I3, I4 `implemented`.
- Orchestrator verification: `dotnet build -warnaserror` 0 warnings; `dotnet test` 1,239 passed (Domain 319, Application 412, Architecture 23, Infrastructure 219, Api integration 266). Read the I4 handler against design §9.1 (parse → `LineProductRequired` → invoice `FOR UPDATE` only → one product/unit query → replace lines → warnings → projection). Migration: none (indexes already in `InvoiceScan`).
- Decisions accepted: AI unit text not stored (match input beside drafts); archived product on a new I4 line → `404 Product.NotFound` (in I4's contract list; avoids a contract change); validator vs. parser split; one extra PK query only on the unit-error path; I4 deletes then inserts lines; status cache answers only `Processing`; contract DTO `InvoiceNewProductDto` (schema name only); one query per strategy proven with an EF command counter.
- Sprint 2 goal met: a photo uploaded to the API becomes a review-ready invoice (end-to-end test with a fake AI handler).

---

# Sprint 4: confirm, sales, dashboard (backend)

- Brief: `docs/agents/briefs/backend-4-confirm-sales-dashboard.md`. Branch `feature/confirm-sales-dashboard`. Worker: general-purpose agent (background); resumed once after a stall.
- Report: "AGENTS.md read: yes", contract deviations NONE. The orchestrator marked I5, S1–S3, P12, D1, D2 `implemented`.
- Sent back once: the worker reported that P8 never returned `409 ProductUnit.InUse` (a used unit hit the restrict FK → 500). Fixed in this step: tenant-filtered EXISTS on invoice and sale lines through `IProductUnitUsageQueries`, and the FK 23503 on the three unit FKs mapped to the same 409 as a safety net.
- Orchestrator verification: `dotnet build -warnaserror` 0 warnings; `dotnet test` 1,341 passed (Domain 337, Application 452, Architecture 23, Infrastructure 231, Api integration 298). Read the lock path: invoice (or sale header) first, then `SELECT 1 FROM products WHERE id = ANY(@ids) AND shop_id = @shop ORDER BY id FOR UPDATE`; cache version bump, status cache and supplier key removal only after commit and only when this request wrote.
- Migration `20261017202302_Sales`: `sales`, `sale_items` (with `line_no`), indexes, real FKs from `stock_movements` to sales.
- Decisions accepted: confirm and S1 manage their own transaction (after-commit work, lost-race answered 200); sale header inserted first as the lock; alias upsert after the confirm save in the same transaction; no opening movement for products created by confirm; `sale_items.line_no` added; negative-balance warnings derived from stored movements; S2 `from` inclusive / `to` exclusive; P12 unit barcode before product barcode; D1 key includes the Cairo date; D2 includes already-expired lines while in stock.
- Known: `invoicesAwaitingReview` on D1 may lag up to 2 minutes (documented); `suppliers:list` key removed but not yet read by any endpoint.

---

# Sprint 5: admin

## Step 5a: admin API

- Brief: `docs/agents/briefs/backend-5a-admin-api.md`. Branch `feature/admin-api`. Worker: general-purpose agent (background).
- **Blocked before code** on a real conflict: rule 9 put `IgnoreQueryFilters()` in Application, which may not reference EF Core; and admin needs `shop_members` by shop id (ADR-0002 update). Orchestrator decision (`bb644f0`): admin ports in Application, the only cross-tenant implementation folder is `Infrastructure/Persistence/Admin/` (architecture-tested); same folder may read members by shop. ADR-0005 and ADR-0002 got dated updates; AGENTS.md rule 9 reworded with the same intent.
- Report: contract deviations NONE. The orchestrator flipped AD1–AD20 to `implemented` and bumped the contract to **v1.6**: `403` listed on A4 (admin token) and AD3 (shop token), as §11.1 already required; the worker added the response types and two tests.
- Found and fixed: `ContractConformanceTests` parsed only one-letter ids, so AD rows were never checked; now `[A-Z]{1,2}`.
- Orchestrator verification: `dotnet build -warnaserror` 0 warnings; `dotnet test` 1,523 passed (Domain 367, Application 537, Architecture 26, Infrastructure 263, Api integration 330). `IgnoreQueryFilters` appears only in three files under `Infrastructure/Persistence/Admin/`; the admin seeder logs only the outcome and user id.
- Migration `20261017212807_PlatformAdmin`: `shops.normalized_name` (backfilled), `suspended_at`, `suspended_reason`; `shop_subscriptions.daily_scan_quota_override` (CHECK 0..10000); `admin_audit_logs` with 3 indexes; trigram index on shop names and a pattern index on user emails.
- Decisions accepted: AD8 inserts the new subscription with one parameterized INSERT in `Persistence/Admin` (no tenant for the stamp); effective quota read from PostgreSQL (no cache to invalidate); AD10/AD19 own their transaction (counter delete and job enqueue after commit); `ProcessInvoiceJob.RerunAsync`; AD8 reason optional; usage field definitions documented; separate AD1 rate-limit policy; seeder retries while PostgreSQL is down; Hangfire session code in memory (single-instance assumption documented).
- Known limitations (admin.md §12): pre-signed image URL uses `Storage__Endpoint` (a public endpoint is needed outside the server network); suspending a shop signs its members out of every shop.

## Step 5b: admin dashboard (Blazor)

- Brief: `docs/agents/briefs/backend-5b-admin-ui.md`. Branch `feature/admin-ui`. Worker: general-purpose agent (background); resumed once after the session ended.
- Report: contract deviations NONE. New `Stockly.Admin` (Blazor WASM, MudBlazor 9.11.0) and `Stockly.Admin.Tests` (bUnit 2.11.3); Admin references Contracts only (two architecture tests).
- Orchestrator verification: `dotnet build -warnaserror` 0 warnings; `dotnet test` 1,593 passed (Domain 367, Application 537, Admin 68, Infrastructure 263, Architecture 28, Api integration 330).
- Decisions accepted: tokens in memory only (reload asks to sign in again); single-flight refresh; English admin UI (`Accept-Language: en`); `MudChart<decimal>`; extra Overview page; client-side 92-day range check; CORS from `Cors__AdminOrigins` only (none by default).
- Environment note: during its smoke test the worker ran the branch API on port 5001 with `.env.example` admin placeholders, which seeded `admin@example.com` with the example password into the local dev DB. Local only; change it before any shared use.
- Follow-up for Sprint 6: an API integration test for the CORS policy.

---

# Sprint 6: smart matching, CI, hardening (backend)

- Brief: `docs/agents/briefs/backend-6-matching-ci-hardening.md` (+ admin CORS integration test, admin CI). Branch `feature/matching-ci-hardening`.
- Report: contract deviations NONE; openapi.json unchanged; no vulnerable packages.
- Orchestrator verification: `dotnet build -warnaserror` 0 warnings; `dotnet test` 1,740 passed (Domain 408, Application 557, Admin 68, Architecture 28, Infrastructure 311, Api integration 368). Coverage (worker, merged): Domain 90.17 %, Application 96.88 %. Only changed assertion-free arrangement: `BarcodeRaceEndpointTests` raw INSERT now sets `latin_key`.
- Migration `20261018130842_MatchingAndImageLifecycle`: `latin_key` on products and aliases (backfilled by `stockly_latin_key`), `invoices.image_deleted_at`, trigram and partial indexes.
- Load test (20 users, dev laptop): P1 p95 15–24 ms; S1 p95 0.6–2.4 s (row-lock waits, worst case one shop's 50 products); I1 p95 0.23–0.29 s; I3 p95 12–15 ms; 0 % errors. Found and fixed thread-pool starvation on scan bursts (ADR-0014).
- ADRs written by the orchestrator: 0012 Latin key, 0013 90-day image retention, 0014 thread-pool floor; backend.md §8 updated.
- Security gaps left (docs/security/owasp-api-review.md): no general per-tenant/IP limit on authenticated endpoints; registration has no bot protection beyond the IP limit; refresh not rate limited; no admin MFA; TLS and private network for Postgres/Redis/MinIO are deployment work; local dev DB still has the example admin password.
- Environment: a separate `stockly_loadtest` database and Redis db 5 were used for the load test; ~25 MB of test images in the dev bucket.

---

# Plans and payments (Paymob), product owner request (2026-10-18)

- The product owner chose paid plans with online payment ("option 3"): Paymob for Visa/cards, Vodafone Cash and other wallets, and kiosk cash; Fawry dropped the same day. ADR-0015 and contract **v1.7** (§12, B1–B6, AD21–AD22) by the orchestrator. Brief `docs/agents/briefs/backend-7-plans-payments.md`, branch `feature/plans-payments`; resumed once after the session ended.
- Orchestrator verification: `dotnet build -warnaserror` 0 warnings; `dotnet test` 1,909 passed (Domain 453, Application 600, Architecture 28, Admin 82, Infrastructure 357, Api integration 389). Read `PaymobHmac`: HMAC-SHA512 over the documented field order, constant-time compare; invalid signature → 401.
- Migration `20261018150034_PlanPayments`: `payments` with idempotency (`shop_id, client_request_id`), callback de-duplication (`provider_transaction_id`), order uniqueness, and indexes for pending, history, admin and the reconcile job.
- Decisions accepted: callback/reconcile/downgrade behind admin ports (cross-tenant, rule 9), system actions without audit rows; extra config `Paymob__ApiKey`, `NotificationUrl`, `CheckoutBaseUrl`, `TimeoutSeconds`; no retries on POSTs to Paymob; Paymob called before the DB transaction (nothing persisted on 502); open-ended same-plan subscriptions are never shortened; late success after cancel/failure becomes Paid.
- **To verify with Paymob test keys** (docs/features/billing.md §12): order id field signed by callbacks, intention expiry unit, wallet and kiosk pay calls, kiosk expiry, HMAC transport, required billing data, inquiry auth, success predicate, notification_url override, Egypt checkout URL.
