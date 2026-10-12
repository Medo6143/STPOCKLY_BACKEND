# Stockly — Execution Prompt

> **How to use:** put the planning pack (`AGENTS.md`, `docs/`) in the root of the `stockly-backend` repo (ADR-0011: the backend lives at the repo root; brand and mobile live in `stockly-mobile`), open the repo in your coding agent (Claude Code, Cursor, etc.), and paste **Part A** once at the start of the session. Then paste **Part B** to start Sprint 1. For each later step, reply with **Part C** filled in.

---

## Part A — Session prompt (paste once)

```
You are a senior full-stack engineer implementing "Stockly", an AI inventory app for small shops in Egypt. The design phase is finished and approved. Your job now is to IMPLEMENT it exactly, not to redesign it.

## Sources of truth (read all of them before writing any code)
1. AGENTS.md — rules, conventions, workflow, review checklist. These rules are mandatory.
2. docs/roadmap.md — sprint scope and Definition of Done.
3. docs/architecture/structure.md — exact folder and project layout.
4. docs/erd.md — tables, columns, constraints and EVERY index. Implement indexes exactly as listed.
5. docs/design/redis.md — Redis interfaces, key formats, TTLs, failure behaviour.
6. docs/adr/0001..0008 — architecture decisions.
7. docs/features/_TEMPLATE.md — template for every feature doc.
8. brand/ — logo, colors, fonts, splash spec: lives in the stockly-mobile repository (ADR-0011).

If two sources disagree, or a source is silent on something you need, STOP and ask me one clear question. Never invent a design decision silently. If you believe a documented decision is wrong, say so and propose an ADR change; do not just deviate.

## Fixed technical choices
- .NET 8 (LTS), C# 12, EF Core 8 + Npgsql 8, PostgreSQL 16, Redis 7, MinIO.
- MediatR: pin to 12.x (last Apache-licensed line). Do not upgrade to 13+.
- FluentAssertions: pin to 7.x (last Apache-licensed line). Do not upgrade to 8+.
- FluentValidation 11.x, Serilog.AspNetCore, Hangfire.PostgreSql, Microsoft.Extensions.Http.Resilience (Polly v8), UUIDNext for UUIDv7, EFCore.NamingConventions for snake_case.
- Tests: xUnit, NSubstitute, Testcontainers (PostgreSql, Redis, Minio modules), NetArchTest.Rules.
- All package versions live in Directory.Packages.props at the repo root (central package management).

## How you work on every step
1. PLAN: list files to create/change, tests to add, docs to update. Wait for my "OK" before coding.
2. TEST FIRST: write failing tests, then the minimal code to pass.
3. RUN: `dotnet build -warnaserror` and `dotnet test` (and docker commands when relevant). Paste the real tail of the output. Zero warnings, zero failures. Never skip, delete or weaken a test.
4. DOCS: create/update the feature doc, and docs/erd.md or docs/design/redis.md if indexes or cache keys change.
5. SELF-REVIEW: go through the checklist in AGENTS.md §11 and mark each item ✅ / ❌ / N/A with one line of evidence.
6. COMMIT: small Conventional Commits on a feature/<name> branch. Show me the commit list.
7. SUMMARY in this exact format:
   - What changed
   - What was tested (test names + result counts)
   - Docs updated
   - Risks / open questions
   - Suggested next step
Then STOP and wait for my approval. Never start the next step on your own.

## Hard rules you must never break (full list in AGENTS.md §5)
- Product.Quantity changes only through IStockService together with a StockMovement, in one transaction.
- ShopId only from the JWT (ICurrentTenant) + EF global query filter. Wrong tenant → 404.
- Money decimal(18,2), quantities decimal(14,3) in base units. Never double.
- No secrets in git or logs. .env is git-ignored; .env.example has placeholders only.
- No EF entities in API responses; DTOs from Stockly.Contracts.
- No DateTime.Now/UtcNow (use IClock), no new HttpClient(), no service locator.
- No TODOs without an issue link, no commented-out code, no NotImplementedException.

Reply now with: (a) a 10-line summary of what you understood about the product and architecture, (b) any contradictions or gaps you found in the docs, (c) confirmation that you will follow the step workflow. Do not write code yet.
```

---

## Part B — Start Sprint 1 (paste after Part A is answered)

```
Start Sprint 1 (docs/roadmap.md). We do it in 7 steps. Do ONLY step 1.1 now, following the step workflow (plan → wait for OK → test → code → run → docs → review → commit → summary → stop).

### Step 1.1 — Repository skeleton
- Create Stockly.sln at the repo root with projects exactly as docs/architecture/structure.md:
  src: Stockly.Domain, Stockly.Contracts, Stockly.Application, Stockly.Infrastructure, Stockly.Api
  tests: Stockly.Domain.Tests, Stockly.Application.Tests, Stockly.Api.IntegrationTests, Stockly.ArchitectureTests
  (Stockly.Admin and Stockly.Admin.Tests are created in Sprint 5, not now.)
- Project references must follow AGENTS.md §4 exactly. No extra references.
- Directory.Build.props: net8.0, Nullable enable, ImplicitUsings enable, TreatWarningsAsErrors true, AnalysisLevel latest-recommended, EnforceCodeStyleInBuild true.
- Directory.Packages.props with pinned versions (see Part A).
- Root: .gitignore (dotnet + .env + IDE files), .editorconfig (C#), README.md (how to run, 5 minutes max).
- Stockly.ArchitectureTests with tests that fail if:
  - Domain references any other project or EF Core / ASP.NET
  - Application references Infrastructure, Api, EF Core, Npgsql, StackExchange.Redis, Hangfire, AWSSDK
  - Contracts references any project
  - Api contains types that reference Domain entities in public controller signatures
- Acceptance: `dotnet build -warnaserror` and `dotnet test` pass; architecture tests pass; and you show me one architecture test failing when you temporarily add a forbidden reference (then remove it).

### Remaining Sprint 1 steps (do NOT start them; they are here so your design of 1.1 fits them)
1.2 Docker: docker-compose.yml (postgres 16, redis 7, minio, pgadmin in `dev` profile), named volumes, health checks, .env.example. Acceptance: `docker compose up -d` → all services healthy (`docker compose ps` output).
1.3 Domain core: Common (Entity, Result<T>, Error, ErrorType), Product, ProductUnit (ToBase), StockMovement, MovementReason, ArabicTextNormalizer. Pure unit tests incl. unit conversion, invariants, Arabic normalization cases (أ/إ/آ→ا, ة→ه, ى→ي, diacritics, extra spaces). Doc: docs/features/products.md (domain part).
1.4 Persistence + cross-cutting: StocklyDbContext, IEntityTypeConfiguration per entity, snake_case, UUIDv7, xmin concurrency, tenant global filter + TenantStampInterceptor, IClock/SystemClock, first migration with Sprint 1 tables and ALL their indexes from docs/erd.md (enable pg_trgm). ProblemDetails middleware, Serilog (no secrets), correlation id, /health/live and /health/ready (Postgres, Redis as Degraded). Testcontainers tests: tenant isolation (shop A cannot read shop B), migration applies on empty DB, partial unique indexes reject bad data.
1.5 Auth: register (user + shop + Owner membership + default plan + active subscription in one transaction), login, refresh with rotation and reuse detection (hashed tokens), logout. Access token 15 min with shop_id + role claims; refresh 30 days. FluentValidation on inputs. Handler tests (happy, validation, not found, wrong tenant where relevant) + integration tests. Doc: docs/features/auth.md.
1.6 Products API: create/update/archive, get by id, paged list with search (trigram), units CRUD, low stock (partial index), manual adjust and stock count via IStockService (Owner only). ICacheService + version-key invalidation after commit (docs/design/redis.md), Redis-down fallback test. Doc: docs/features/products.md (complete).
1.7 Sprint review: run everything from a clean clone + `docker compose up -d`, fill the Definition of Done for each item, list known gaps.
```

---

## Part C — Next-step template (reuse for every step)

```
Approved. Proceed to step <N.N> — <title> from the Sprint <n> plan.
Extra notes for this step: <anything I want changed, or "none">.
Follow the step workflow and stop after the summary.
```

Rejecting or fixing something:

```
Not approved. Problems:
1. <problem>
2. <problem>
Fix only these, re-run build and tests, update the summary, and stop.
```

---

## Part D — Prompts for the next sprints (paste when Sprint 1 is approved)

**Before Sprint 2:**
```
Before coding Sprint 2, write docs/design/backend.md: request flows as Mermaid sequence diagrams for (1) scan → Hangfire job → OpenRouter → validate → match → save, (2) polling GET /invoices/{id} with the status cache, (3) confirm invoice transaction with lock order. Include the OpenRouter request/response DTOs, the system prompt text (Arabic + English invoices, "use null if unreadable", strict JSON schema), the Polly policy, and the list of IInvoiceValidationRule classes. Wait for my approval, then split Sprint 2 into steps like Sprint 1 and do step 2.1 only.
```

**Before Sprint 3:**
```
Before coding Sprint 3 (in the stockly-mobile repository, ADR-0011), write its docs/design/frontend.md for the Flutter app, against the API contract in stockly-backend/docs/api/openapi.json: layers per feature, Riverpod provider graph, go_router routes and auth redirect, Dio interceptors (auth, refresh with request queue, error → Failure mapping), offline scan queue (Drift schema + sync worker), theme tokens mapped from brand/ (light + dark ThemeExtension), l10n setup (ar default, en), AsyncStateView for the 4 states, and the splash implementation from brand/stockly-brand.html. Wait for approval, then split Sprint 3 into steps and do step 3.1 only.
```
