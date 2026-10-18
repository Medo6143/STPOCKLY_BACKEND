# OWASP API Security Top 10 (2023) review

Sprint 6, 2026-10-18, branch `feature/matching-ci-hardening`, contract v1.6. Scope: `Stockly.Api` (shop API and admin
API), its Infrastructure, the Hangfire dashboard, and `Stockly.Admin` as a client. For each risk: how Stockly addresses
it, the evidence (code and tests), what this review fixed, and what is left. Paths are relative to the repository root;
test classes are under `tests/`.

## API1:2023 Broken Object Level Authorization

**How.** Every tenant entity has an EF global query filter on `shop_id` from the JWT (`ICurrentTenant`, never from the
request; AGENTS.md rules 8–10, ADR-0002); without a tenant the filter matches nothing (fail closed). Raw SQL repeats
`shop_id = @shop` (`StockService`, `ProductSimilarityQueries`). Another shop's id answers **404**, never 403.
Cross-tenant access exists only in `Infrastructure/Persistence/Admin/` behind admin ports used only by
`Application/Features/Admin`.

**Evidence.** `src/Stockly.Infrastructure/Persistence/StocklyDbContext.cs` (filter), `TenantIsolationTests`,
`TenantFilterTests`, `IgnoreQueryFiltersTests`, `AdminPortUsageTests` (architecture), wrong-tenant tests in every
endpoint suite (e.g. `ProductsEndpointTests`, `InvoiceReviewEndpointTests`, `SalesEndpointTests`),
`TrigramMatchingPersistenceTests.Match_ProductOfAnotherShop_NoMatch` and `FindSimilar_NoTenant_NothingAndNoQuery` (new).

**Gaps.** None found.

## API2:2023 Broken Authentication

**How.** JWT HMAC-SHA256 with a key of at least 32 bytes from the environment (start-up fails otherwise); validation
pins issuer, audience, signing key, algorithm (`ValidAlgorithms = HS256`, so `alg: none` and algorithm confusion are
rejected), lifetime with zero clock skew and required expiry. Refresh tokens are random, stored hashed, rotated on
every use; reuse revokes the family (rule 13). Login lockout after repeated failures; per-IP rate limits on login,
register and admin login (`429 Auth.TooManyRequests`). Admin tokens carry a platform-role claim and no shop claim; the
Hangfire dashboard needs a one-time code redeemed for an HttpOnly, `SameSite=Strict` cookie (Secure on HTTPS).

**Evidence.** `src/Stockly.Infrastructure/Identity/JwtOptions.cs`, `JwtTokenServiceTests`, `StartupConfigurationTests`
(missing/short key), `RefreshAndLogoutEndpointTests`, `RefreshTokenRepositoryTests`, `LockoutEndpointTests`,
`RateLimitEndpointTests`, `AdminAuthEndpointTests`, `HangfireDashboardEndpointTests`.

**Fixed now.** Forwarded headers are configurable (`ForwardedHeaders__Enabled`, `__KnownProxies`, `__KnownNetworks`,
one hop), so behind a reverse proxy the per-IP limits see the real client and not the proxy, and only trusted proxies
can set the client IP (`src/Stockly.Api/Configuration/ForwardedHeadersSetup.cs`, `SecurityHardeningTests`).

**Gaps.** The refresh endpoint has no per-IP limit (tokens are 256-bit random and hashed, so guessing is not
practical); no MFA for the platform admin (single admin, strong password policy) — consider for a multi-admin setup.

## API3:2023 Broken Object Property Level Authorization

**How.** Requests bind to explicit `record` DTOs in `Stockly.Contracts`; responses are contract records built by
projections, never EF entities (rule 27). Server-owned fields (`shop_id`, `quantity`, `created_by`, status, audit
fields) are not in any request DTO, so mass assignment is impossible. Stock changes only through `IStockService`
(rule 1). `RawAiJson` is returned only by the admin API (AD18).

**Evidence.** `ControllerSignatureTests` (architecture: no entity in a controller signature), `ContractConformanceTests`
and `OpenApiShapeTests` (every response shape matches the contract), FluentValidation validators per command.

**Gaps.** None found.

## API4:2023 Unrestricted Resource Consumption

**How.** Every list is paged with `pageSize ≤ 100` (rule 30); invoice and sale lines are capped at 100; the scan accepts
8 MB images (`413`), a per-shop per-minute rate limit and a daily quota per plan; the AI call has a 30 s timeout and at
most 2 retries, only from the background job (rules 14, 17); AI output over 100 items is rejected (rule 15).

**Evidence.** `ScanEndpointTests` (413, 429 rate limit and quota with `Retry-After`), paging validators and their tests,
`OpenRouterInvoiceExtractorTests` (timeout, retries), `docs/operations/load-test.md`.

**Fixed now.** Kestrel accepts request bodies up to **1 MiB** (default was 30 MB); the scan endpoint raises its own
limit to 9 MB. No `Server` header (`src/Stockly.Api/Program.cs`, `RequestLimits.cs`,
`SecurityHardeningTests.Kestrel_NoServerHeaderAndOneMebibyteBodyLimit`). The load test found thread-pool starvation on
a scan burst (I1 p95 22.6 s); fixed with a thread-pool floor (`ThreadPoolMinThreads=64`, I1 p95 0.23–0.29 s).

**Gaps.** No general per-tenant or per-IP rate limit on the other authenticated endpoints (P1, S1, D1, …): a stolen or
abusive token can still send many cheap requests. Add a per-shop token-bucket policy once real traffic shows the
normal rate.

## API5:2023 Broken Function Level Authorization

**How.** Authorization is declarative: the default policy requires a valid shop token of an active shop; owner-only
endpoints use the `Owner` policy (role claim); every admin controller uses `PlatformAdmin` (platform-role claim **and**
no shop claim, so a shop token never opens the admin API and an admin token never opens a shop endpoint); suspended
shops are blocked except for logout. Failures are ProblemDetails `401`/`403` (contract §1.8).

**Evidence.** `src/Stockly.Api/Authentication/AuthenticationSetup.cs`, `ActiveShopHandler.cs`,
`AdminAuthEndpointTests` (shop token on the admin API → 403), `AdminShopEndpointTests` (suspended shop),
`HangfireDashboardEndpointTests`.

**Gaps.** None found.

## API6:2023 Unrestricted Access to Sensitive Business Flows

**How.** The costly flow is the AI scan: per-shop rate limit, daily quota per plan (admin override and reset are
audited), AI result cache and duplicate-photo reuse so a repeated photo costs nothing, and idempotent `clientRequestId`
for scans and sales (rule 20). Registration is rate limited per IP. The AI never changes stock (rule 19); only a user's
confirm does, idempotently with row locks (rules 3, 4).

**Evidence.** `ScanEndpointTests`, `RedisScanLimiterTests`, `ConfirmEndpointTests` (double confirm), `ConcurrencyTests`,
`SalesEndpointTests` (repeat returns the same sale), `AdminShopEndpointTests` (quota override/reset audited).

**Gaps.** Account creation has no bot protection beyond the per-IP limit (no CAPTCHA, no email or phone
verification): a botnet could create many shops, each with its own daily scan quota, and spend AI credits. Before
opening registration publicly, add phone verification or manual shop activation.

## API7:2023 Server Side Request Forgery

**How.** The Api never fetches a URL supplied by a client or by the AI. Images arrive as upload bytes; storage keys
are built by the server (`InvoiceImageKeys`); the OpenRouter base URL and model come from configuration; the AI
answer is parsed as data, never followed. Pre-signed image URLs are generated, not fetched.

**Evidence.** `src/Stockly.Application/Features/Invoices/InvoiceImageKeys.cs`,
`src/Stockly.Infrastructure/Ai/OpenRouter/OpenRouterOptions.cs` (validated at start-up), `ScanInvoiceCommandHandler`.

**Gaps.** None found.

## API8:2023 Security Misconfiguration

**How.** Secrets only from `.env`/environment (rule 11), validated at start-up with messages that name the variable but
never print the value; logs never contain secrets, tokens, images or raw AI JSON (rule 12); unexpected errors become
ProblemDetails with a `traceId` and no exception detail (rule 28); the OpenAPI document is not served in Production.

**Evidence.** `GlobalExceptionHandler.cs`, `ProblemDetailsTests`, `StartupConfigurationTests`, `LoggingConfiguration.cs`.

**Fixed now.**
- Security headers on every response, errors included: `X-Content-Type-Options: nosniff`, `X-Frame-Options: DENY`,
  `Referrer-Policy: no-referrer`, `Permissions-Policy`, and `Content-Security-Policy: default-src 'none';
  frame-ancestors 'none'` on the JSON API (not on `/hangfire`, which serves its own scripts)
  (`src/Stockly.Api/Middleware/SecurityHeadersMiddleware.cs`, `SecurityHardeningTests`).
- HSTS in Production (`app.UseHsts()`; applies to HTTPS requests, i.e. behind the TLS proxy with forwarded headers).
- CORS only from `Cors__AdminOrigins` (no default, no credentials, fixed methods and headers) now has API tests:
  configured origin allowed, other origins and an empty setting get no CORS headers, invalid values fail start-up
  (`AdminCorsTests`).
- Production errors proven to carry no exception message, type or stack trace, and no OpenAPI document
  (`SecurityHardeningTests.Production_UnexpectedError_NoExceptionDetailsAndNoOpenApi`).
- The load test's fake AI (`Ai__UseFake`) cannot be switched on outside the `LoadTest` environment (`FakeAiGuardTests`).
- CI fails on a known-vulnerable NuGet package, direct or transitive (`.github/workflows/backend.yml`); actions are
  pinned by commit SHA; the workflow token is read-only.

**Gaps.** TLS is expected at the reverse proxy (the Api has no HTTPS redirection of its own); the local compose stack
uses plain connections to PostgreSQL, Redis and MinIO (bound to 127.0.0.1) — production needs TLS or a private network
for them. The pre-signed image URL uses `Storage__Endpoint`, which must be reachable by the admin's browser (admin.md
§12). The local dev database still holds the example admin password seeded during step 5b (change it before any
shared use).

## API9:2023 Improper Inventory Management

**How.** One API version (v1), one binding contract (`docs/api/contract.md`, v1.6) listing every endpoint with its
status; the OpenAPI document is generated from the running code and committed; tests fail when an implemented endpoint
is missing from the contract or the committed spec is stale; CI fails on any drift of `docs/api/openapi.json`. Admin
endpoints live under `/api/admin`, separate policies. The test-only endpoints exist only in the test assembly.

**Evidence.** `ContractConformanceTests`, `OpenApiDocumentTests`, `OpenApiShapeTests`, `.github/workflows/backend.yml`.

**Gaps.** None found for v1. Plan a versioning rule (path or header) before the first breaking change.

## API10:2023 Unsafe Consumption of APIs

**How.** OpenRouter output is untrusted (rule 15): strict DTO, `json_object` response format, FluentValidation, at most
100 items, numbers and dates parsed by the normalizer, totals cross-checked (`TotalMismatch`, rule 16); the raw JSON
is stored for audit and never logged; timeouts, retries only on 429/5xx, fallback model; every call recorded
(rule 18). The AI never changes stock (rule 19). OpenRouter is called only from `Infrastructure/Ai` inside the
Hangfire job (rule 14).

**Evidence.** `ExtractedInvoiceValidator`, `ExtractedInvoiceNormalizerTests`, `OpenRouterInvoiceExtractorTests` (valid,
malformed, empty, too many items, 429, 500, timeout), `ProcessInvoiceJobTests`, `FakeOpenRouterHandlerTests`.

**Gaps.** None found.

## Summary of gaps left

1. No general per-tenant rate limit on authenticated endpoints other than scan and auth (API4).
2. Registration has no bot protection beyond per-IP limits (API6).
3. Refresh endpoint not rate limited; no MFA for the platform admin (API2).
4. TLS at the proxy and for PostgreSQL/Redis/MinIO in production; public storage endpoint for pre-signed URLs (API8).
5. Local dev database: example admin password from step 5b (API8, operational).
