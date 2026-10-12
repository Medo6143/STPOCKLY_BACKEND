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
