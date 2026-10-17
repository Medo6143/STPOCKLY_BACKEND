Read AGENTS.md fully before starting.

# Brief: backend-6-matching-ci-hardening

Repository: `stockly-backend` (`D:\FLUTTER\stockly`), branch `feature/sprint-6` (already checked out, based on `main` with Sprints 2–5 merged) · Lane: BACKEND · Contract version: **v1.5** (`docs/api/contract.md`)

You have not seen the conversation that produced this brief. Everything you need is here or in the files it links. If something you need is missing or contradicts AGENTS.md, the design or the contract, **stop and report** instead of guessing.

## 1. Goal and why
Sprint 6: ready for the first real shops. Smarter product matching across Arabic/English spellings, CI on GitHub Actions with a coverage gate, a load test of the hot endpoints, a security review, image lifecycle, and a backup plan.

## 2. Scope
**You may change / create:** `src/**` (except `src/Stockly.Admin` unless a security fix needs it), `tests/**` (including new `tests/Stockly.LoadTests`), `.github/workflows/**`, `docker-compose.yml` (MinIO lifecycle in `minio-init` only), `.env.example`, `docs/erd.md`, `docs/design/redis.md`, `docs/features/invoice-scan.md` (matching section), `docs/security/owasp-api-review.md` (new), `docs/operations/backup.md` (new), `docs/operations/image-lifecycle.md` (new), `docs/operations/load-test.md` (new), `docs/api/openapi.json` (regenerated only).

**You must NOT touch:** `docs/api/contract.md`, `docs/adr/**` (if a decision is hard to reverse, report it and the orchestrator writes the ADR), `docs/agents/**`, `docs/sprints/**`, `AGENTS.md`, `CLAUDE.md`, `.env`. `Directory.Packages.props` only for `coverlet.collector` / `ReportGenerator`-free coverage (use `dotnet test --collect:"XPlat Code Coverage"` with coverlet; anything else: stop and report).

**Git and machine safety:** never run git commands that change the working tree or history. Never push (workflows are written, not run on GitHub). Never kill processes by image name; stop only processes you started, by PID. Never delete Docker containers, images or volumes, never `docker system prune`. Never change anything outside this repository. Never call the real OpenRouter API.

## 3. What to build
1. **Trigram matching** (`docs/design/backend.md` §8 "Left for Sprint 6"): `TrigramMatchStrategy` (Order 30) using `pg_trgm` `similarity()` on `product_aliases.normalized_alias` and `products.normalized_name` (GIN `gin_trgm_ops` indexes; create in a migration if missing, document in `docs/erd.md`), threshold from config (`Matching:TrigramThreshold`, default `0.45`), confidence = similarity rounded to 2 decimals, one query per strategy for all lines (no N+1), archived products never match. Cross-script ("بنادول اكسترا" ↔ "Panadol Extra"): add a deterministic Arabic→Latin transliteration for the comparison key as a pure domain function with unit tests (no package), stored as `products.latin_key` / `product_aliases.latin_key` columns filled on write and backfilled in the migration; the strategy compares both keys and takes the higher similarity. Aliases learned from other suppliers (supplier-agnostic alias match) at confidence × 0.9. Confidence is already in `InvoiceItemDto.matchConfidence`.
2. **CI** `.github/workflows/backend.yml`: on push and PR; services Postgres 16 and Redis 7 not needed when Testcontainers is used (runner has Docker); `dotnet build -warnaserror`, `dotnet test` with coverage, gate **≥ 80% line coverage on Domain and Application** (fail the job otherwise), upload `openapi.json` diff check (fail if `OpenApiDocumentTests` would change it). `.github/workflows/admin.yml` for `Stockly.Admin` build + bUnit tests. Pin action versions by full commit SHA with a version comment.
3. **Coverage now:** measure locally; if Domain or Application is below 80%, add meaningful tests until it passes (never test-only trivia like auto-properties).
4. **Load test** `tests/Stockly.LoadTests` (console app, plain `HttpClient` via `IHttpClientFactory`, no new package): scenarios I3 polling (status cache hit path), P1 list, S1 sales with distinct `clientRequestId`s, I1 scan with the fake AI handler configured (`Ai:UseFake=true` in a `LoadTest` environment only; it must be impossible in Production, guarded and tested). Report p50/p95/p99 and error rate; results and how to run in `docs/operations/load-test.md`. Excluded from `dotnet test`.
5. **Security review** `docs/security/owasp-api-review.md`: OWASP API Security Top 10 (2023), one section each: how Stockly addresses it, evidence (file/test), gaps. Fix every gap you can within scope (e.g. security headers, request size limits, CORS from config only, `ForwardedHeaders` configurable for deployment, JWT validation parameters, ProblemDetails never leak stack traces in Production) with tests; list what is left.
6. **Image lifecycle:** MinIO bucket lifecycle rule expiring invoice images after 90 days (`minio-init` with `mc ilm rule add --expire-days 90`), plus a daily Hangfire job that marks `invoices.image_deleted_at` for images past 90 days so AD18 returns `imageUrl: null` (migration + test). Document in `docs/operations/image-lifecycle.md`.
7. **Backup plan** `docs/operations/backup.md`: `pg_dump` daily with retention, restore drill steps, MinIO bucket replication or `mc mirror`, RPO/RTO targets, what is not backed up (Redis is cache only). Add `scripts/backup-postgres.ps1` and `.sh` that read connection settings from `.env` (never hardcode secrets).

## 4. Critical rules
All of AGENTS.md §5. In particular 12 (no secrets/images/raw AI JSON in logs), 21–25 cache, 31–33 DB (migrations reviewed, index per query, no N+1), 40–42 hygiene.

## 5. Checks
```bash
dotnet build -warnaserror
dotnet test
dotnet test --collect:"XPlat Code Coverage"   # Domain and Application >= 80% lines
```
Zero warnings, zero failures. Never skip, delete or weaken a test.

## 6. Do not commit, push or merge.

## 7. Final report (exact format)
```
AGENTS.md read: yes
Rules applied: <numbers and one line each>
Files changed: <list by folder>
Check outputs: <tail of build and test, test counts, coverage % for Domain and Application>
Migration: <name and one-line SQL summary>
Load test: <p50/p95/p99 and error rate per scenario>
Security gaps left: <list or "none">
Decisions taken: <list, with reason>
Open questions: <list or "none">
Contract deviations: NONE   (anything else: stop before finishing and report it)
```
