Read AGENTS.md fully before starting.

# Brief: backend-5a-admin-api

Repository: `stockly-backend` (`D:\FLUTTER\stockly`), branch `feature/admin-api` (already checked out, based on `main` with Sprints 2 and 4 merged) · Lane: BACKEND · Contract version: **v1.5** (`docs/api/contract.md`)

You have not seen the conversation that produced this brief. Everything you need is here or in the files it links. If something you need is missing or contradicts AGENTS.md, ADR-0005 or the contract, **stop and report** instead of guessing.

## 1. Goal and why
Sprint 5, first half: the API the platform owner needs to run the business. Admin login, shops (list, detail, suspend/activate), plans and subscriptions, quota override and reset, AI usage and cost per day / shop / model, failed scans with the image and raw AI JSON and a re-run, the audit log, and the Hangfire dashboard behind admin. The Blazor UI comes in step 5b and consumes exactly these endpoints.

## 2. Scope
**You may change / create:** `src/Stockly.Domain/**`, `src/Stockly.Application/**`, `src/Stockly.Infrastructure/**`, `src/Stockly.Api/**`, `src/Stockly.Contracts/**`, `tests/**`, `.env.example` (placeholders `Admin__Email`, `Admin__Password`, `Admin__FullName`), `docs/erd.md`, `docs/design/redis.md`, `docs/features/admin.md` (new, from `_TEMPLATE.md`), `docs/api/openapi.json` (regenerated only).

**You must NOT touch:** `docs/api/contract.md`, `docs/adr/**`, `docs/agents/**`, `docs/sprints/**`, `docs/design/backend.md`, `docs/roadmap.md`, `AGENTS.md`, `CLAUDE.md`, `.env`, `Directory.Packages.props` (no new packages expected; if one is needed, stop and report). Do not create `src/Stockly.Admin` (step 5b).

**Git and machine safety:** never run git commands that change the working tree or history. To undo your own edit, edit the file back. Never kill processes by image name; stop only processes you started, by PID. Never delete Docker containers, images or volumes, never `docker system prune`. Never change anything outside this repository.

## 3. Binding sources
- Contract **§11** (AD1–AD20, schemas, auth and roles, audit rule, Hangfire session), §1, §9 new codes.
- ADR-0005: admin handlers only in `Application/Features/Admin`, the only place allowed to call `IgnoreQueryFilters()` (add/extend the architecture test), policy `PlatformAdmin`, every admin write audited in the same transaction.
- Schema additions (migration, read the SQL, document in `docs/erd.md`): `shop_subscriptions.daily_scan_quota_override int NULL` with `CHECK (0..10000)`; `shops.suspended_at timestamptz NULL`, `shops.suspended_reason text NULL`; `admin_audit_logs` columns per contract §11.1 if missing; any index each new admin filter/sort needs (AD4 search, AD15/AD16 aggregation by `created_at`, AD17 failed invoices by `created_at`, AD20 filters).
- The scan quota (step 2a) must use `effectiveDailyScanQuota` = override ?? plan quota; quota cache invalidation documented in `docs/design/redis.md`. AD10 resets the Redis counter through `IScanQuotaCounter`.
- AD19 re-run: enqueue `ProcessInvoiceJob` with a "bypass AI cache, do not charge quota" flag; `AiUsageRecord` still written for every call (rule 18).
- AD18 `imageUrl`: pre-signed GET via `IFileStorage`, 5 minutes. `rawAiJson` is returned but never logged (rule 12).
- Admin seeding at start-up from configuration when no PlatformAdmin exists; never log the password.

## 4. Critical rules (copied from AGENTS.md)
- 8–10: shop endpoints keep the tenant filter; an admin token has no tenant, so tenant endpoints reject it (`403 Auth.Forbidden`).
- 9: `IgnoreQueryFilters()` only under `Application/Features/Admin`; every admin write writes an `AdminAuditLog`.
- 11–13: secrets in `.env` only; never log secrets, tokens, passwords, images or raw AI JSON; refresh tokens hashed, rotated, reuse revokes the family.
- 21–25 cache rules; 26–33 API/DB rules (paging max 100, index per filter/sort documented, projections, no N+1); 40–42 hygiene, `IClock`.

## 5. Steps
1. Tests first: every admin handler (happy path, validation, not found, non-admin token → 403, shop token → 403, admin token on a shop endpoint → 403), audit row written in the same transaction (and none on idempotent repeats), suspend revokes refresh tokens and the next shop call gets `403 Shop.Suspended`, quota override changes the effective quota of the next scan, AD10 resets the counter, AD14 zero-filled days and Cairo boundaries, AD19 only for `Failed`, bypasses cache, no quota charge, architecture test for `IgnoreQueryFilters`, admin seeding (created once, not duplicated), Hangfire session code single-use and expiring.
2. Implement; migration (read the SQL).
3. Docs: `docs/features/admin.md` (API part), `docs/erd.md`, `docs/design/redis.md`; regenerate `docs/api/openapi.json`; `ContractConformanceTests` passes with AD1–AD20 marked implemented in its expectations if it tracks status (the contract table itself is changed by the orchestrator).

## 6. Checks
```bash
dotnet build -warnaserror
dotnet test
```
Zero warnings, zero failures. Never skip, delete or weaken a test.

## 7. Do not commit, push or merge.

## 8. Final report (exact format)
```
AGENTS.md read: yes
Rules applied: <numbers and one line each>
Files changed: <list by folder>
Check outputs: <tail of dotnet build and dotnet test, with test counts>
Migration: <name and one-line SQL summary>
Decisions taken: <list, with reason>
Open questions: <list or "none">
Contract deviations: NONE   (anything else: stop before finishing and report it)
```
