# Feature: Platform admin (API and dashboard)

Status: Done (API, step 5a; Blazor dashboard `Stockly.Admin`, step 5b) · Sprint: 5 · Owner: backend lane

## 1. Purpose
The platform owner runs the business from `Stockly.Admin`: signs in as a platform admin, finds and inspects shops, suspends or reactivates them, moves them between plans, raises or resets their daily scan quota, watches AI usage and cost, debugs and re-runs failed scans, reads the audit log of every admin write, and opens the Hangfire dashboard. This document covers the API (§3–§12) and the Blazor dashboard that consumes it (§13). Contract: `docs/api/contract.md` §11 (v1.5), ADR-0005 (with its update of 2026-10-18) and ADR-0002 (update 2026-10-18).

## 2. User flow
1. The admin signs in on the admin login page (AD1). The token has `platform_role=PlatformAdmin` and no shop. The app refreshes it with AD2 and signs out with AD3.
2. "المتاجر" lists shops with search and status filter (AD4). A row opens the shop page (AD5): members, plan, quota, counts, today's scans and the AI cost of 30 days.
3. From the shop page the admin suspends or reactivates the shop (AD6/AD7), changes its plan (AD8), sets or removes a quota override (AD9) or resets today's scans (AD10). Every write except AD8 asks for a reason. AD8 accepts an optional reason.
4. "الباقات" lists, creates and edits plans (AD11–AD13).
5. "استهلاك الذكاء الاصطناعي" shows usage per day, per shop and per model (AD14–AD16).
6. "المسح الفاشل" lists failed scans (AD17). One opens with its image, raw AI output and AI calls (AD18), and can be re-run (AD19).
7. "سجل المراجعة" lists admin writes (AD20).
8. "Hangfire" asks the API for a one-time URL (`POST /api/admin/hangfire/session`) and opens it in a new tab.

## 3. Endpoints
All under policy `PlatformAdmin` (claim `platform_role=PlatformAdmin` and no `shop_id` claim) except AD1/AD2 (anonymous). A shop token on an admin endpoint gets `403 Auth.Forbidden`. An admin token on any shop endpoint, including A4 logout, gets `403 Auth.Forbidden`. Code: `Api/Controllers/Admin/*` → `Application/Features/Admin/<UseCase>` → ports in `Application/Abstractions/Admin` → `Infrastructure/Persistence/Admin` (the only folder that calls `IgnoreQueryFilters()`). DTOs: `Contracts/Admin`.

| # | Method | Path | Request | Response | Status codes |
|---|---|---|---|---|---|
| AD1 | POST | `/api/admin/auth/login` | `{ email, password }` | `200 AdminAuthResponse` | 200, 400, 401, 403, 423, 429 |
| AD2 | POST | `/api/admin/auth/refresh` | `{ refreshToken }` | `200 AdminAuthResponse` | 200, 400, 401 |
| AD3 | POST | `/api/admin/auth/logout` | `{ refreshToken }` | `204` (idempotent; reuses A4's `LogoutCommand`) | 204, 400, 401 |
| AD4 | GET | `/api/admin/shops` | `search`, `status`, `page`, `pageSize` | `200 PagedResponse<AdminShopListItem>`, newest first | 200, 400, 401, 403 |
| AD5 | GET | `/api/admin/shops/{id}` | | `200 AdminShopDetail` | 200, 401, 403, 404 |
| AD6 | POST | `/api/admin/shops/{id}/suspend` | `{ reason }` | `204` | 204, 400, 401, 403, 404 |
| AD7 | POST | `/api/admin/shops/{id}/activate` | `{ reason }` | `204` | 204, 400, 401, 403, 404 |
| AD8 | PUT | `/api/admin/shops/{id}/subscription` | `{ planId, endsOn, reason }` | `200 AdminSubscriptionDto` | 200, 400, 401, 403, 404 |
| AD9 | PUT | `/api/admin/shops/{id}/quota-override` | `{ dailyScanQuota, reason }` | `200 AdminSubscriptionDto` | 200, 400, 401, 403, 404 |
| AD10 | POST | `/api/admin/shops/{id}/quota/reset` | `{ reason }` | `204` | 204, 400, 401, 403, 404 |
| AD11 | GET | `/api/admin/plans` | | `200 PlanDto[]` (active first, then by code) | 200, 401, 403 |
| AD12 | POST | `/api/admin/plans` | `{ code, name, dailyScanQuota, maxMembers, monthlyPrice, isActive }` | `201 PlanDto` | 201, 400, 401, 403, 409 |
| AD13 | PUT | `/api/admin/plans/{id}` | `{ name, dailyScanQuota, maxMembers, monthlyPrice, isActive }` | `200 PlanDto` | 200, 400, 401, 403, 404 |
| AD14 | GET | `/api/admin/ai-usage/daily` | `from`, `to` (dates), `shopId` | `200 AiUsageDay[]`, oldest first, zero days included | 200, 400, 401, 403 |
| AD15 | GET | `/api/admin/ai-usage/by-shop` | `from`, `to`, `page`, `pageSize` | `200 PagedResponse<AiUsageByShop>`, highest cost first | 200, 400, 401, 403 |
| AD16 | GET | `/api/admin/ai-usage/by-model` | `from`, `to` | `200 AiUsageByModel[]`, highest cost first | 200, 400, 401, 403 |
| AD17 | GET | `/api/admin/invoices/failed` | `shopId`, `page`, `pageSize` | `200 PagedResponse<AdminFailedInvoice>`, newest first | 200, 400, 401, 403 |
| AD18 | GET | `/api/admin/invoices/{id}` | | `200 AdminInvoiceDetail` | 200, 401, 403, 404 |
| AD19 | POST | `/api/admin/invoices/{id}/rerun` | `{ reason }` | `202 InvoiceDetail` (status `Processing`) | 202, 400, 401, 403, 404, 409 |
| AD20 | GET | `/api/admin/audit-logs` | `targetType`, `targetId`, `adminUserId`, `page`, `pageSize` | `200 PagedResponse<AuditLogEntry>`, newest first | 200, 400, 401, 403 |
| — | POST | `/api/admin/hangfire/session` | | `200 { url }` (`/hangfire?code=…`) | not in the endpoint index or `openapi.json` (operations, like `/health`) |

Field meanings that the contract leaves open:
- `AiUsageDay.calls`, `AiUsageByShop.calls`, `AiUsageByModel.calls`: OpenRouter calls, retries included. Cache hits (Redis or reuse of an earlier extraction) are not counted here; AD14 reports them in `cacheHits`. `failures`: calls whose outcome is not `Succeeded`. `avgLatencyMs`: the average over calls.
- `AdminFailedInvoice.attempts`: the invoice's OpenRouter calls (usage rows that are not cache hits). `updatedAt`: `processed_at`, or `created_at` when it was never processed.
- `AdminShopListItem.scansToday` and `AdminShopDetail.scansToday`: the shop's invoices created since Africa/Cairo midnight, read from PostgreSQL. This count does not change when AD10 resets the quota counter.
- `AuditLogEntry.before` / `after`: camelCase JSON objects of the changed fields, enums as names. AD10 has none. AD6 `after` also carries `revokedRefreshTokens`.

## 4. Data model changes
Migration **`PlatformAdmin`** (`20261017212807_PlatformAdmin`), erd §3.1 and §3.6:

| Object | Definition | Serves |
|---|---|---|
| `shops.normalized_name` | `text NOT NULL`, `ArabicTextNormalizer` of the name, set by the Domain. Existing rows are backfilled in SQL by the same mapping | AD4 name search |
| `shops.suspended_at`, `shops.suspended_reason` | `timestamptz NULL`, `text NULL`; set by AD6, cleared by AD7 | AD5 |
| `shop_subscriptions.daily_scan_quota_override` | `int NULL`, `CHECK (… IS NULL OR … BETWEEN 0 AND 10000)` (`ck_shop_subscriptions_daily_scan_quota_override`) | AD9; the scan's effective quota |
| `admin_audit_logs` | `id`, `admin_user_id` (FK users, restrict), `action varchar(64)`, `target_type varchar(64)`, `target_id uuid`, `reason text NULL`, `before jsonb NULL`, `after jsonb NULL`, `created_at` | every admin write |
| `ix_shops_created_at` | `(created_at DESC)` | AD4 without a status filter |
| `ix_shops_status_created_at` (existing) | `(status, created_at DESC)` | AD4 with a status filter |
| `ix_shops_normalized_name_trgm` | GIN `gin_trgm_ops` on `normalized_name` | AD4 `ILIKE '%term%'` |
| `ix_users_normalized_email_pattern` | `(normalized_email text_pattern_ops)` | AD4 owner email prefix `LIKE 'TERM%'` |
| `ix_admin_audit_logs_target_type_target_id_created_at` | `(target_type, target_id, created_at DESC)` | AD20 by target (and by type alone, as its prefix) |
| `ix_admin_audit_logs_admin_user_id_created_at` | `(admin_user_id, created_at DESC)` | AD20 by admin; FK support |
| `ix_admin_audit_logs_created_at` | `(created_at DESC)` | AD20 without filters |

The AI usage, failed invoice and invoice detail queries use indexes that already existed (`ix_ai_usage_records_created_at`, `ix_ai_usage_records_shop_id_created_at`, `ix_ai_usage_records_invoice_id`, `ix_invoices_status_created_at_failed`, `ix_invoices_shop_id_status_created_at`, `ix_invoices_shop_id_created_at`). Each is listed next to its query in `docs/erd.md`.

## 5. Integrations
| | Redis (quota counter) | MinIO (image URL) | Hangfire (re-run, dashboard) |
|---|---|---|---|
| Why | AD10 deletes today's counter | AD18 shows the scan photo | AD19 queues the extraction again; the dashboard is for operations |
| Endpoint / operation | `DEL stockly:{shopId}:quota:scan:{yyyyMMdd}` through `IScanQuotaCounter.ResetAsync` | HEAD, then a pre-signed GET valid 5 minutes (`IFileStorage.GetDownloadUrlAsync`) | `ProcessInvoiceJob.RerunAsync` on queue `invoices`; dashboard at `/hangfire` |
| Auth | Redis password | the API's MinIO user signs the URL | admin token for the session code; HttpOnly cookie for the dashboard |
| Timeout / retry | 250 ms, no retry | SDK defaults | job: 3 retries, then `Failed` (same filter as the scan job) |
| Failure behaviour | Redis down: AD10 still returns 204 with the audit row; the limiter logs a throttled warning; the scan's PostgreSQL fallback count cannot be reset | object missing: `imageUrl: null`; MinIO down: 500 | database down at dashboard start: the dashboard is built on the first authorized request |
| Cost | none | none | a re-run calls OpenRouter (paid) and writes `AiUsageRecord`s; quota is not charged |

## 6. Cache keys
| Key | TTL | Invalidated by |
|---|---|---|
| `stockly:{shopId}:quota:scan:{yyyyMMdd}` (existing) | 26 h | expires; deleted by AD10 |
| `stockly:{shopId}:invoice:{id}:status` (existing) | 10 min | overwritten by AD19 with `Processing` after its commit |

The effective daily quota (plan quota or override) is not cached. The scan reads it from PostgreSQL every time, so AD8, AD9 and AD13 apply from the next scan with nothing to invalidate. See `docs/design/redis.md`.

## 7. Business rules
- Only a user with `platform_role = PlatformAdmin` can sign in through AD1. A valid shop user gets `401 Auth.InvalidCredentials`, so AD1 does not reveal who is an admin. Lockout (`423`) and the per-IP limit (`429`, policy `admin-auth-login`, counted separately from A2) are the same as A2.
- AD2 rotates within the token's family like A3. A token whose user is not a platform admin (a shop refresh token) gets `401 Auth.InvalidRefreshToken`. A3 rejects an admin's refresh token the same way, because the admin has no shop membership.
- Admin and shop roles are kept apart. The seed never promotes an existing user, and the API cannot make a platform admin a shop member.
- The first admin is created at start-up from `Admin__Email`, `Admin__Password` and `Admin__FullName`, only when no platform admin exists. If the email belongs to a user, nothing is created and an error is logged. The seed runs during host start-up. When PostgreSQL is down it retries every 10 s in the background. The password is never logged.
- Every admin write (AD6–AD10, AD12, AD13, AD19) writes exactly one `admin_audit_logs` row in the same transaction as the change. Idempotent repeats write nothing: AD6 on a suspended shop, AD7 on an active shop, and AD9 with the current value.
- AD6, AD7, AD9, AD10 and AD19 need a non-blank `reason`. Blank gives `400 Admin.ReasonRequired`; more than 500 characters gives `400 Validation.Failed`. AD8's reason is optional and kept when given, because contract §9 lists `Admin.ReasonRequired` only for those five.
- AD6 suspends the shop and revokes every active refresh token of every member, in the same transaction. Refresh tokens are per user, so a member of several shops signs in again everywhere. The next call with the old access token gets `403 Shop.Suspended` (checked on every request). AD7 does not restore sessions; members sign in again.
- AD8 ends the active subscription (`status = Ended`, `ends_on` = today in Cairo) and starts a new one from today. `endsOn` must not be before today. The quota override is carried over. An inactive plan gives `400 Plan.Inactive`.
- AD9 accepts `0..10000`, or `null` to remove the override. The scan's quota is `override ?? plan quota`.
- AD10 commits its audit row first, then deletes the Redis counter.
- Plan names (decision 3 of the orchestrator, 2026-10-18): plans keep `name_ar` and `name_en` (no schema change). AD12 and AD13 write the contract's single `name` to both columns. AD11–AD13 and `AdminSubscriptionDto.planName` return the name in the request's `Accept-Language` (default `ar`). The seeded plan therefore shows "الأساسية" in Arabic and "Standard" in English. `code` is `^[a-z0-9-]{2,32}$` and immutable. A taken code gives `409 Plan.CodeTaken`, both on the pre-check and on `ux_plans_code`.
- AD14–AD16: `from`/`to` are Africa/Cairo dates, both included. `to` defaults to today and `from` to 29 days before `to`. `to - from` is at most 92 days and `to >= from`; otherwise `400 Validation.Failed` on `to`. A Cairo day runs from its local midnight in UTC, and summer time is handled. AD14 returns every day of the range, with zeros where there was no usage.
- AD19 is allowed only for `Failed` (otherwise `409 Invoice.NotRerunnable`). In one transaction it locks the invoice row and moves it `Failed → Processing` (the failure reason is cleared; the raw AI JSON stays until the next run replaces it) with the audit row. After the commit it queues `ProcessInvoiceJob.RerunAsync` and writes the status cache. The re-run bypasses both the Redis AI cache and the reuse of an earlier extraction of the same photo, and OpenRouter is called. Every call writes an `AiUsageRecord` (rule 18). Quota is charged only by I1, so the re-run does not touch it.
- AD18's `rawAiJson` is returned to the admin and never logged (rule 12). `imageUrl` is null when the object is gone.
- Hangfire dashboard: `POST /api/admin/hangfire/session` returns `/hangfire?code=…`. The code is 32 random bytes, kept only as its SHA-256, single use, and valid 60 s by `IClock`. `/hangfire?code=…` exchanges it for the `stockly_hangfire` cookie (HttpOnly, `SameSite=Strict`, path `/hangfire`, Secure on HTTPS, 15 minutes) and redirects to `/hangfire/`. The cookie value is the admin id and expiry, encrypted with ASP.NET Core Data Protection. Every other `/hangfire` request without a valid cookie gets `401 Auth.Unauthenticated`.
- **Single API instance (decision of 2026-10-18):** the one-time codes are kept in the API process's memory (`InMemoryHangfireSessionCodes`), and the Data Protection keys use the default local key ring. Running several API instances needs the codes in Redis and a shared key ring.

## 8. Error cases
| Case | Error code | HTTP | User message (ar / en) |
|---|---|---|---|
| Blank reason (AD6, AD7, AD9, AD10, AD19) | `Admin.ReasonRequired` | 400 | السبب مطلوب / Reason required |
| Unknown shop (AD5–AD10) | `Shop.NotFound` | 404 | لم يتم العثور على المتجر / The shop was not found |
| Unknown plan (AD8, AD13) | `Plan.NotFound` | 404 | لم يتم العثور على الباقة / The plan was not found |
| Plan code taken (AD12) | `Plan.CodeTaken` | 409 | توجد باقة أخرى بهذا الرمز / A plan with this code already exists |
| Inactive plan assigned (AD8) | `Plan.Inactive` | 400 | لا يمكن إسناد باقة غير مفعّلة إلى متجر / An inactive plan cannot be assigned |
| Re-run of a non-failed invoice (AD19) | `Invoice.NotRerunnable` | 409 | يمكن إعادة معالجة الفاتورة الفاشلة فقط / Only a failed invoice can be processed again |
| Unknown invoice (AD18, AD19) | `Invoice.NotFound` | 404 | (existing text) |
| Shop token on an admin endpoint, admin token on a shop endpoint | `Auth.Forbidden` | 403 | (existing text) |
| Missing or invalid dashboard cookie or code | `Auth.Unauthenticated` | 401 | (existing text) |

## 9. Configuration / env vars
| Name | Example | Secret? |
|---|---|---|
| `Admin__Email` | `admin@example.com` | no (never logged anyway) |
| `Admin__Password` | `change-me-admin-password-1` | **yes** |
| `Admin__FullName` | `Platform Admin` | no |
| `Cors__AdminOrigins` | `http://127.0.0.1:5080` | no. Comma-separated browser origins allowed to call the API (CORS policy `admin`, no credentials). Empty: no cross-origin call is allowed |
| `ApiBaseUrl` (in `src/Stockly.Admin/wwwroot/appsettings.json`) | `http://127.0.0.1:5000/` | no. Public file downloaded by the browser; never put a secret there |

When any of the three is missing, seeding is skipped and an information line is logged.

## 10. Mobile / Admin screens
Mobile: none. Admin (`Stockly.Admin`, step 5b): login, overview, shops list and detail, plans, AI usage, failed scans list and detail, audit log, Hangfire button. Each screen has the 4 states (loading, empty, error, data). The admin UI is English only, left to right (brief 5b); the screen names in §2 are the Arabic equivalents. Details in §13.

## 11. Tests
- Domain: `PlatformAdminShopTests` (suspend/activate idempotency, normalized name, quota override range, subscription start/end, plan update), `AdminAuditLogTests`, `InvoiceTests.Rerun_*`.
- Application: `AdminAuthHandlerTests` (AD1/AD2: admin-only, lockout, rotation, reuse, shop token), `AdminShopHandlerTests` (AD4–AD10: happy paths, audit once, none on repeats, reason/validation, not found, unauthenticated, reset after the save), `AdminPlanAndUsageHandlerTests` (AD11–AD16: language, code taken incl. race, precision, zero-filled days, Cairo boundaries and summer time, range validation), `AdminInvoiceAndMiscHandlerTests` (AD17–AD20, re-run only `Failed`, queued after the commit with a status-cache write, signed image URL, Hangfire session, seed created once / never promotes), `ProcessInvoiceCommandHandlerTests.Handle_AdminRerun_*` (bypasses DB reuse and AI cache, usage row written).
- Infrastructure (Testcontainers): `AdminPersistenceTests` (cross-tenant list/search/detail, escaped wildcards, subscription replacement inside a transaction, quota CHECK, effective quota, member token revocation, daily usage by Cairo day, by shop/model ordering and paging, failed list and invoice detail of any shop, lock-and-save re-run, audit log filters and JSON, plan code race, platform admin accounts), `RedisScanLimiterTests.Reset_*` (incl. Redis down), `S3FileStorageTests.GetDownloadUrl_*` (pre-signed GET serves the bytes; missing → null), `InMemoryHangfireSessionCodesTests` (single use, 60 s), `JwtTokenServiceTests.CreateAdminAccessToken_*`, `MigrationTests` (new tables, indexes, CHECK).
- Api (real host): `AdminAuthEndpointTests` (seeded once, admin token claims, shop user 401, token families, logout, 403 matrix both ways, 401 anonymous), `AdminShopEndpointTests` (suspend → `403 Shop.Suspended` and revoked refresh, one audit row on repeats, activate, reason/404, override changes the next scan's quota and AD10 gives it back, list/search/detail, subscription change carrying the override, audit log), `AdminPlanUsageInvoiceEndpointTests` (plans and language, usage zero-fill and range 400, failed list, signed image, re-run 202/409/404, end-to-end re-run that bypasses the cache, calls the AI once and charges no quota), `HangfireDashboardEndpointTests` (single-use code, cookie flags, 401 without or with a forged or expired cookie, not in OpenAPI).
- Admin UI (bUnit, `tests/Stockly.Admin.Tests`): every page in its loading, empty, error (detail + trace id) and data states against a fake `HttpMessageHandler`; `BearerTokenHandlerTests` (attaches the token, refreshes once on 401 and retries once, concurrent 401s share one refresh, signs out when the refresh fails, no second refresh); reason dialogs keep Confirm disabled while the reason is blank; costs render with their 8 decimals unchanged and sums are exact (`DisplayTests`); login (validation, error, return URL, no open redirect); logout calls AD3 then forgets the tokens; Hangfire button opens the one-time URL on the API host.
- Architecture: `ProjectReferenceTests` (Admin references only Contracts) and `LayerDependencyTests.Admin_DoesNotDependOnServerLayers`; `IgnoreQueryFiltersTests` (only `Infrastructure/Persistence/Admin`), `AdminPortUsageTests` (only `Application/Features/Admin` uses the admin ports; cross-tenant implementations live in `Persistence/Admin`), `ShopMemberAccessTests` (by-shop reads only there).

## 12. Known limitations
- One API instance (see §7). Multi-instance needs the dashboard codes in Redis and a shared Data Protection key ring (release sprint).
- The pre-signed image URL uses the configured storage endpoint (`Storage__Endpoint`). A browser outside the server's network needs a public endpoint (release sprint).
- The `shops.normalized_name` backfill of existing rows follows the normalizer in SQL. `lower()` follows the database collation, while the Domain uses invariant lower case. Every new or renamed shop gets the Domain value.
- `scansToday` counts invoices created today and does not drop after AD10, which only resets the quota counter.
- A suspension signs members out of every shop they belong to, because refresh tokens are per user.

## 13. Admin dashboard (`Stockly.Admin`, step 5b)

Blazor WebAssembly + MudBlazor, served as static files, referencing **only** `Stockly.Contracts` (ADR-0005, architecture-tested). It calls the API exactly like the mobile app: JSON per contract §1, `Authorization: Bearer`, `Accept-Language: en`.

**Run locally.** The API must run from this branch (it needs the `admin` CORS policy) with `Cors__AdminOrigins=http://127.0.0.1:5080` (in `.env`), then `dotnet run --project src/Stockly.Admin` and open http://127.0.0.1:5080. The API address is `ApiBaseUrl` in `wwwroot/appsettings.json` (default `http://127.0.0.1:5000/`). Use `127.0.0.1` for both (not `localhost` for one of them): the Hangfire cookie is `SameSite=Strict`, so the dashboard tab must be same-site with the admin.

| Page | Route | Endpoints | Notes |
|---|---|---|---|
| Login | `/login` | AD1 | Return URL limited to paths inside the app (no open redirect) |
| Overview | `/` | AD4 (counts), AD14, AD16, AD17, AD22 | KPI cards, 30-day cost chart, cost by model, revenue card (paid amount and count of the last 30 days, by method; own four states), latest failed scans |
| Shops | `/shops` | AD4 | Search (debounced), status filter, server paging |
| Shop | `/shops/{id}` | AD5–AD10, AD11, AD21 | Payments section (AD21 with `shopId`, paged, own four states); suspend/activate, reset quota and quota override ask a required reason; change plan offers active plans only, end date and reason optional (AD8) |
| Plans | `/plans` | AD11–AD13 | Monthly price shown prominently, free plans (price 0) marked `Free`; create and edit in a dialog; `409 Plan.CodeTaken` and other errors stay in the dialog |
| AI usage | `/ai-usage` | AD14–AD16 | Date range picker (default last 30 days; at most 92 days between the dates, checked before calling), totals, daily chart, by shop (paged) and by model |
| Failed scans | `/failed-scans`, `/failed-scans/{id}` | AD17–AD19 | Optional `?shopId=` filter; detail shows the photo (`imageUrl`), raw AI JSON in a read-only code view, AI calls; re-run asks a reason |
| Payments | `/payments` | AD21 | Plan payments of every shop (billing, ADR-0015): status filter, optional `?shopId=`, paged table (shop linked to its page, plan, method, amount, status chip with the failure reason, created, paid, Paymob transaction id). Read only: refunds are manual in the Paymob dashboard |
| Audit log | `/audit-log` | AD20 | Filters target type, target id, admin id (also from `?targetType=&targetId=`); expandable before/after JSON |
| Hangfire | app bar button | `POST /api/admin/hangfire/session` | Opens `{api}/hangfire?code=…` in a new tab (`noopener`) |

Decisions:
- **Tokens in memory only.** `AuthSession` (singleton per tab) keeps the access and refresh tokens in memory; nothing is written to `localStorage` or `sessionStorage`. A reload signs the admin out (trade-off accepted for XSS safety). Tokens are never logged or rendered.
- **Refresh.** `BearerTokenHandler` (a `DelegatingHandler` on the typed `AdminApiClient` from `IHttpClientFactory`) adds the token; on `401` it calls AD2 through `TokenRefresher` once and retries once. Concurrent `401`s share one in-flight refresh; a request whose token was already replaced retries with the new one. A failed refresh signs out and the router sends the admin to the login page. AD1/AD2 use a separate typed client without the handler.
- **Logout** calls AD3 with the refresh token, then forgets the session even if the API cannot be reached.
- **Decimals.** Money and USD costs are parsed with `decimal.Parse(..., CultureInfo.InvariantCulture)` and never pass through `double`; USD costs are shown with their 8 decimals unchanged and totals are decimal sums. The chart is `MudChart<decimal>`.
- **Errors.** Every API failure becomes an `ApiProblem` (RFC 7807 `title`, `detail`, `code`, `traceId`, `errors`); pages show the detail, code and trace id with a retry button, actions show them in a snackbar. Network/CORS failures show `Client.Unreachable`.
- **Theme.** Light and dark palettes; the app bar toggle switches them (starts from the system preference, kept in memory).

Known limitations: a page reload needs a new sign-in; the pre-signed image URL must be reachable from the browser (see §12); the dashboard is English only.

## 14. Payments (billing, contract §12.4, ADR-0015)

- **AD21** `GET /api/admin/payments?shopId=&status=&page=&pageSize=` and **AD22** `GET /api/admin/payments/summary?from=&to=` are read-only admin endpoints (no audit entry). They go through `IAdminPaymentQueries` in `Infrastructure/Persistence/Admin` (rule 9). Indexes: `docs/erd.md` §3.8.
- The platform side of payments that has no shop token (the Paymob callback, the reconcile job, the daily downgrade) reaches across tenants too, so it lives in `Application/Features/Admin` behind `IAdminPaymentRepository` (same rule 9 boundary). These are system actions: no admin user, no `AdminAuditLog`.
- Dashboard: the Payments page, the overview revenue card and the shop's payments (§13). bUnit: `PaymentsPageTests` (each of the three in loading, empty, error, data), `PlansPageTests.Render_FreeAndPaidPlans_*`.
- Full feature doc: `docs/features/billing.md`.
