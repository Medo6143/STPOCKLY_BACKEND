# AGENTS.md — Stockly

This file is the contract for anyone writing code in this repo: humans and AI agents. Read it fully before your first change. If a task conflicts with a rule here, **stop and ask**. Do not silently break a rule.

---

## 1. What Stockly is

Stockly is an inventory product for small shops in Egypt (pharmacies, groceries, mobile shops). The owner photographs a supplier invoice, the backend extracts it with an AI vision model (OpenRouter), the user reviews and confirms, and stock updates through stock movements. Shops record sales on a quick-sale screen so stock goes down too. A Blazor admin dashboard lets the platform owner manage shops, plans, quotas and AI cost.

Users speak Arabic first. Arabic (RTL) and English, light and dark, from day one.

Key docs:

| Doc | What it holds |
|---|---|
| `docs/roadmap.md` | Sprints, scope, definition of done |
| `docs/architecture/structure.md` | Folder structure of this repository (API + admin) |
| `docs/erd.md` | Data model, every index and constraint |
| `docs/design/redis.md` | Redis jobs, key catalogue, TTLs, failure behaviour |
| `docs/adr/` | Architecture decisions, numbered `NNNN-title.md`. Read before changing anything they cover. |
| `docs/features/` | One doc per feature, from `_TEMPLATE.md` |
| `docs/api/openapi.json` | API contract consumed by the mobile app (from step 1.4b) |

**Repositories (ADR-0011).** This repository, `stockly-backend`, holds the API and the Blazor admin. The Flutter app lives in `stockly-mobile` and consumes the API through the OpenAPI spec at `docs/api/openapi.json`; mobile-only rules live in that repository.

---

## 2. Stack

| Layer | Technology |
|---|---|
| Admin | Blazor WebAssembly + MudBlazor (`Stockly.Admin`) |
| Backend | .NET 10 (LTS) Web API, EF Core 10 + Npgsql, MediatR, FluentValidation, Serilog, ASP.NET Core Identity |
| Database | PostgreSQL 16 in Docker, extensions `pg_trgm` |
| Cache | Redis 7 in Docker |
| Storage | MinIO (S3-compatible) in Docker |
| AI | OpenRouter `/api/v1/chat/completions`, **backend only** |
| Jobs | Hangfire, PostgreSQL storage, schema `hangfire` |
| Resilience | Polly via `Microsoft.Extensions.Http.Resilience` |
| Tests | xUnit, FluentAssertions, NSubstitute, Testcontainers, NetArchTest · bUnit |
| CI | GitHub Actions |

---

## 3. Commands

```bash
# infrastructure
cp .env.example .env
docker compose up -d                       # postgres, redis, minio
docker compose --profile dev up -d         # + pgadmin

# backend (from the repo root)
dotnet build -warnaserror
dotnet test
dotnet tool restore                        # dotnet-ef pinned in .config/dotnet-tools.json
dotnet ef migrations add <Name> -p src/Stockly.Infrastructure -s src/Stockly.Api
dotnet ef database update      -p src/Stockly.Infrastructure -s src/Stockly.Api --connection "<ConnectionStrings__Postgres from .env>"
dotnet run --project src/Stockly.Api

# admin
dotnet run --project src/Stockly.Admin
```

A change is not finished until the relevant commands pass with **zero warnings**.

---

## 4. Architecture

```
Api ──► Infrastructure ──► Application ──► Domain
 │                            ▲    │
 ├────────────────────────────┘    ▼
 └──────────────────────────► Contracts ◄── Admin
```

| Project | Owns | References (exactly) | Must never reference |
|---|---|---|---|
| `Stockly.Domain` | Entities, enums, value objects, domain rules, `Result`/`Error` | none; no packages (ADR-0010) | any project or package |
| `Stockly.Contracts` | Request/response DTOs shared by Api and Admin | none | anything but BCL |
| `Stockly.Application` | MediatR commands/queries, handlers, validators, interfaces it needs | Domain, Contracts | EF Core, Npgsql, HttpClient, Redis, Hangfire, AWS SDK |
| `Stockly.Infrastructure` | EF Core, Identity, OpenRouter client, Redis, MinIO, Hangfire | Application | Api |
| `Stockly.Api` | Controllers, middleware, auth wiring, DI composition (composition root) | Application, Infrastructure, Contracts | business logic |
| `Stockly.Admin` | Blazor UI | Contracts | Application, Infrastructure, Domain |

Api references Application directly because, as the composition root, it uses Application command and query types explicitly; a transitive reference would hide a real dependency.

These boundaries are enforced by `tests/Stockly.ArchitectureTests`. A failing architecture test is a design problem, not a test problem.


---

## 5. Non-negotiable rules

### Data & stock
1. Never write `Product.Quantity` directly. Only `IStockService` changes it, in the same transaction as a new `StockMovement` (ADR-0001).
2. Stock movements are append-only. Corrections are new movements.
3. Confirm invoice and create sale are **idempotent**: status check + row lock + unique indexes on (invoice_item_id, reason) and (sale_item_id).
4. Lock order is always: invoice (or sale) first, then products ordered by `Id`. Never lock in another order.
5. Quantities are stored in the product's base unit, `decimal` / `numeric(14,3)`. Conversion only through `ProductUnit.ToBase()` (ADR-0003).
6. Money is `decimal` / `numeric(18,2)`, never `double`. Currency is EGP.
7. Expiry and batch are informational in v1. No lot tracking (ADR-0004).

### Tenancy & security
8. `ShopId` comes from the JWT via `ICurrentTenant`, never from the request. Every tenant entity has the EF global query filter (ADR-0002).
9. `IgnoreQueryFilters()` is allowed only under `Application/Features/Admin`. Every admin write writes an `AdminAuditLog` (ADR-0005).
10. Wrong-tenant access returns **404**, never 403.
11. Secrets (OpenRouter key, JWT key, DB/Redis/MinIO passwords) live in `.env` / user-secrets only. `.env` is git-ignored. `.env.example` has placeholders.
12. Never log secrets, tokens, passwords, full images or raw AI JSON. Log IDs and sizes.
13. Refresh tokens are stored hashed, rotated on every use, and reuse revokes the chain.

### AI
14. OpenRouter is called only from `Infrastructure/Ai`, only inside the Hangfire job, never inside an HTTP request.
15. AI output is untrusted: strict DTO, FluentValidation, max 100 items, raw JSON stored in `Invoice.RawAiJson`.
16. If `|sum(lines) − total| > 2% of total`, status is `NeedsReview` with warning `TotalMismatch`.
17. `temperature = 0`, `response_format = json_object`, model and fallback model from config, timeout 30 s, retry max 2 with backoff on 429/5xx only.
18. Every call (including retries and cache hits) writes an `AiUsageRecord`.
19. The AI never changes stock. Only a user confirmation does.

### Offline & idempotent writes
20. `POST /invoices/scan` and `POST /sales` require a client-generated `ClientRequestId`. A repeat returns the existing resource (ADR-0006).

### Caching (details: `docs/design/redis.md`)
21. All cache access goes through `ICacheService`, `IAiResultCache`, `IInvoiceStatusCache`, `IScanRateLimiter`, `IScanQuotaCounter`.
22. Tenant keys start with `stockly:{shopId}:`. Platform keys with `stockly:platform:`. Every value key has a TTL.
23. Invalidate **after** commit, by bumping `stockly:{shopId}:ver:catalog`. Never invalidate by `SCAN`/pattern.
24. Never read a quantity from cache to make a write decision.
25. Redis failure degrades to Postgres with a warning. It never fails a request.

### API
26. Controllers only map HTTP ↔ MediatR. No `if` on business state in a controller.
27. Never return EF entities. Responses are records from `Stockly.Contracts`.
28. Expected failures return `Result<T>`; the Api maps `Error.Type` to status codes. Unexpected exceptions go to the global middleware and become RFC 7807 ProblemDetails with a `traceId`.
29. Async everywhere with `CancellationToken` passed to the end.
30. Lists are paged (`page`, `pageSize` max 100). No unbounded queries.

### Database
31. Schema changes only through EF migrations. Review the generated SQL before committing. Never edit the DB by hand.
32. Every new filter or sort ships with its index, documented in `docs/erd.md` next to the query it serves.
33. No N+1: use projections (`Select` to DTO) for reads. `AsNoTracking` for all queries.

### Mobile
34–39. Reserved: the mobile rules moved to the `stockly-mobile` repository (`AGENTS.md` there). Numbers are kept so references such as "rule 42" stay valid.

### Code hygiene
40. No magic strings, no dead code, no commented-out code, no `TODO` without an issue link.
41. No `NotImplementedException` in a real implementation.
42. No `DateTime.Now` / `UtcNow`: use `IClock`. No `new HttpClient()`: use `IHttpClientFactory`. No service locator.

---

## 6. SOLID, applied

| Principle | Here it means |
|---|---|
| S | One handler per use case. `OpenRouterInvoiceExtractor` only talks to OpenRouter. Repositories fetch, handlers decide, controllers map. |
| O | New invoice check = new `IInvoiceValidationRule` class registered in DI. New AI provider = new `IAiInvoiceExtractor`. No growing `switch` on type or provider. |
| L | Fakes behave like real implementations: same `Result` errors, same exceptions. |
| I | Small interfaces per need (`IProductRepository`, `ICacheService`, `IScanQuotaCounter`...). No generic `IRepository<T>` god-interface. |
| D | Application defines interfaces, Infrastructure implements them, constructor injection only. |

Name the principle in the PR when a design choice is not obvious. Do not add abstractions that remove no real coupling.

---

## 7. Conventions

**C#**
- File-scoped namespaces, nullable enabled, `TreatWarningsAsErrors`, analyzers on (`Directory.Build.props`).
- Classes `sealed` unless designed for inheritance. DTOs and commands are `record`s.
- Feature folders: `Features/<Area>/<UseCase>/` holds command/query, handler, validator, and nothing else.
- Entities protect invariants: private setters, factory methods (`Product.Create(...)`), methods for behavior (`invoice.MarkNeedsReview(...)`).
- EF configuration in `IEntityTypeConfiguration<T>` classes, one per entity.
- Central package versions in `Directory.Packages.props`.

**Naming**
- Tests: `Method_Scenario_ExpectedResult`.
- Branches: `feature/<name>`, `fix/<name>`, `chore/<name>`, `docs/<name>`.
- Commits: Conventional Commits (`feat:`, `fix:`, `test:`, `refactor:`, `chore:`, `docs:`). Small, one concern each.

---

## 8. Testing rules

- Every handler: happy path, validation failure, not found, wrong tenant.
- Domain rules (stock movement, unit conversion, totals tolerance, Arabic normalization) have pure unit tests.
- `OpenRouterInvoiceExtractor` against a fake `HttpMessageHandler`: valid JSON, malformed JSON, empty items, too many items, 429, 500, timeout.
- Repositories and the confirm transaction use Testcontainers (real Postgres), including a **concurrent double-confirm** test.
- Redis adapters: Testcontainers Redis, plus a "Redis down" test proving fallback.
- Coverage target ≥ 80% on Domain and Application.
- Never delete, skip or weaken a test to make the build pass.

---

## 9. Documentation rules

- A feature is not done without `docs/features/<feature>.md` (from `_TEMPLATE.md`), updated in the same change as the code.
- New index → update `docs/erd.md`. New cache key → update `docs/design/redis.md`.
- A decision that is hard to reverse → new `docs/adr/NNNN-title.md`.
- ADR changes: a clarification goes in a dated "Update" section of the same ADR. A changed decision gets a new ADR that supersedes the old one (mark the old one "Superseded by ADR-NNNN").

---

## 10. Workflow for every task

1. **Plan**: files to touch, tests to add, docs to update. Wait for approval on large or unclear tasks.
2. **Test first**: write the failing test.
3. **Implement** the smallest change that passes. No refactor mixed with a feature.
4. **Run** build, tests, analyze. Zero warnings.
5. **Document** in the same change.
6. **Self-review** with section 11.
7. **Summarize**: what changed · what was tested · docs updated · risks / open questions · suggested next step.

If unsure, ask **one** clear question instead of guessing.

---

## 11. Review checklist

- [ ] Layer boundaries respected (architecture tests green)
- [ ] SOLID respected, no god classes, interfaces where they remove real coupling
- [ ] Tenant filter applies; no `IgnoreQueryFilters` outside Admin
- [ ] Input validated (FluentValidation); AI output validated
- [ ] No secrets or PII in code or logs
- [ ] Stock changes transactional, idempotent, correct lock order
- [ ] No N+1; index added and documented for each new filter/sort
- [ ] Cache keys prefixed, TTL set, invalidated after commit
- [ ] Error paths tested, including wrong tenant
- [ ] Migration added and SQL reviewed
- [ ] Feature doc created or updated
- [ ] Works from a clean `docker compose up -d` with `.env.example` values
