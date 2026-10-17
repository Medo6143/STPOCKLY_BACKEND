Read AGENTS.md fully before starting.

# Brief: backend-5b-admin-ui

Repository: `stockly-backend` (`D:\FLUTTER\stockly`), branch `feature/sprint-5-admin-ui` (already checked out, based on `main` with step 5a merged) · Lane: BACKEND (Admin) · Contract version: **v1.5** (`docs/api/contract.md`)

You have not seen the conversation that produced this brief. Everything you need is here or in the files it links. If something you need is missing or contradicts AGENTS.md, ADR-0005 or the contract, **stop and report** instead of guessing.

## 1. Goal and why
Sprint 5, second half: `Stockly.Admin`, the Blazor WebAssembly + MudBlazor dashboard the platform owner uses. It calls the admin API (contract §11, built in step 5a) with JWT, exactly like the mobile app calls its endpoints.

## 2. Scope
**You may change / create:** `src/Stockly.Admin/**` (new project), `tests/Stockly.Admin.Tests/**` (new, bUnit), `Stockly.sln` (or `.slnx`), `Directory.Packages.props` (only `MudBlazor`, `Microsoft.AspNetCore.Components.WebAssembly`, `Microsoft.AspNetCore.Components.WebAssembly.DevServer`, `Microsoft.AspNetCore.Components.Authorization`, `Microsoft.Extensions.Http`, `bunit`; anything else: stop and report), `tests/Stockly.ArchitectureTests/**` (Admin references only Contracts), `src/Stockly.Api/**` only for CORS for the admin origin from configuration, `.env.example` (placeholders), `docs/features/admin.md` (UI part), `README.md` (how to run the admin).

**You must NOT touch:** `docs/api/contract.md`, `docs/adr/**`, `docs/agents/**`, `docs/sprints/**`, `AGENTS.md`, `CLAUDE.md`, `.env`, any Application/Domain/Infrastructure code.

**Git and machine safety:** never run git commands that change the working tree or history. Never kill processes by image name; stop only processes you started, by PID. Never delete Docker containers, images or volumes. Never change anything outside this repository.

## 3. What to build
- Project `src/Stockly.Admin` (Blazor WASM, .NET 10, MudBlazor), referencing **only** `Stockly.Contracts` (ADR-0005; architecture test).
- Auth: login page (AD1), tokens in memory plus refresh token in `localStorage` is **not** allowed (XSS); keep the refresh token in `sessionStorage` only if needed for reload, documented as a trade-off, or in memory only (re-login after reload). Prefer memory only. A `DelegatingHandler` adds the bearer token and refreshes once on 401 (AD2), single-flight. Logout (AD3).
- Pages (MudBlazor, light/dark toggle, English UI is enough for the admin; RTL not required): Shops list (search, status filter, paging) → Shop detail (members, subscription, counts, scans today, AI cost 30 days; actions suspend/activate, change plan, quota override, reset quota, each with a required reason dialog) · Plans (list, create, edit) · AI usage (daily chart with MudBlazor charts, by shop table, by model table, date range picker, max 92 days) · Failed scans (list → detail with image from `imageUrl`, raw AI JSON in a read-only code view, usage rows, re-run with reason) · Audit log (filters, paging) · "Open Hangfire" button using the Hangfire session endpoint.
- Every page handles loading, empty, error (ProblemDetails `detail` + `traceId`) and data states.
- Money and costs are shown from the decimal strings without float conversion (`decimal.Parse` with invariant culture).
- API base URL from `wwwroot/appsettings.json` (`ApiBaseUrl`), no secrets in the client.

## 4. Critical rules (copied from AGENTS.md)
- Admin references Contracts only; never Application, Infrastructure, Domain.
- 11–12: no secrets in the client; never log tokens.
- 26–28: the admin consumes Contracts records; errors are ProblemDetails with `code`.
- 40–42: no magic strings, no dead code, no TODO without issue link, `IHttpClientFactory` / typed clients, no `new HttpClient()`.

## 5. Tests (bUnit)
Every page in its 4 states with a fake `HttpMessageHandler`; the auth handler (attaches token, refreshes once on 401, single-flight, logs out on refresh failure); reason dialogs block empty reasons; costs render with 8 decimals unchanged. Architecture test: Admin references only Contracts.

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
Decisions taken: <list, with reason>
Open questions: <list or "none">
Contract deviations: NONE   (anything else: stop before finishing and report it)
```
