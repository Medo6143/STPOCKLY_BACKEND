# Feature: Platform admin (API)

Status: Done (API, step 5a; the Blazor UI is step 5b) · Sprint: 5 · Owner: backend lane

## 1. Purpose
The platform owner runs the business from `Stockly.Admin`: signs in as a platform admin, finds and inspects shops, suspends or reactivates them, moves them between plans, raises or resets their daily scan quota, watches AI usage and cost, debugs and re-runs failed scans, reads the audit log of every admin write, and opens the Hangfire dashboard. This document covers the API that the Blazor UI (step 5b) consumes. Contract: `docs/api/contract.md` §11 (v1.5), ADR-0005 (with its update of 2026-10-18) and ADR-0002 (update 2026-10-18).

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

When any of the three is missing, seeding is skipped and an information line is logged.

## 10. Mobile / Admin screens
Mobile: none. Admin (`Stockly.Admin`, step 5b): login, shops list and detail, plans, AI usage, failed scans, audit log, Hangfire link. Each screen has the 4 states (loading, empty, error, data), Arabic first (RTL).

## 11. Tests
- Domain: `PlatformAdminShopTests` (suspend/activate idempotency, normalized name, quota override range, subscription start/end, plan update), `AdminAuditLogTests`, `InvoiceTests.Rerun_*`.
- Application: `AdminAuthHandlerTests` (AD1/AD2: admin-only, lockout, rotation, reuse, shop token), `AdminShopHandlerTests` (AD4–AD10: happy paths, audit once, none on repeats, reason/validation, not found, unauthenticated, reset after the save), `AdminPlanAndUsageHandlerTests` (AD11–AD16: language, code taken incl. race, precision, zero-filled days, Cairo boundaries and summer time, range validation), `AdminInvoiceAndMiscHandlerTests` (AD17–AD20, re-run only `Failed`, queued after the commit with a status-cache write, signed image URL, Hangfire session, seed created once / never promotes), `ProcessInvoiceCommandHandlerTests.Handle_AdminRerun_*` (bypasses DB reuse and AI cache, usage row written).
- Infrastructure (Testcontainers): `AdminPersistenceTests` (cross-tenant list/search/detail, escaped wildcards, subscription replacement inside a transaction, quota CHECK, effective quota, member token revocation, daily usage by Cairo day, by shop/model ordering and paging, failed list and invoice detail of any shop, lock-and-save re-run, audit log filters and JSON, plan code race, platform admin accounts), `RedisScanLimiterTests.Reset_*` (incl. Redis down), `S3FileStorageTests.GetDownloadUrl_*` (pre-signed GET serves the bytes; missing → null), `InMemoryHangfireSessionCodesTests` (single use, 60 s), `JwtTokenServiceTests.CreateAdminAccessToken_*`, `MigrationTests` (new tables, indexes, CHECK).
- Api (real host): `AdminAuthEndpointTests` (seeded once, admin token claims, shop user 401, token families, logout, 403 matrix both ways, 401 anonymous), `AdminShopEndpointTests` (suspend → `403 Shop.Suspended` and revoked refresh, one audit row on repeats, activate, reason/404, override changes the next scan's quota and AD10 gives it back, list/search/detail, subscription change carrying the override, audit log), `AdminPlanUsageInvoiceEndpointTests` (plans and language, usage zero-fill and range 400, failed list, signed image, re-run 202/409/404, end-to-end re-run that bypasses the cache, calls the AI once and charges no quota), `HangfireDashboardEndpointTests` (single-use code, cookie flags, 401 without or with a forged or expired cookie, not in OpenAPI).
- Architecture: `IgnoreQueryFiltersTests` (only `Infrastructure/Persistence/Admin`), `AdminPortUsageTests` (only `Application/Features/Admin` uses the admin ports; cross-tenant implementations live in `Persistence/Admin`), `ShopMemberAccessTests` (by-shop reads only there).

## 12. Known limitations
- One API instance (see §7). Multi-instance needs the dashboard codes in Redis and a shared Data Protection key ring (release sprint).
- The pre-signed image URL uses the configured storage endpoint (`Storage__Endpoint`). A browser outside the server's network needs a public endpoint (release sprint).
- The `shops.normalized_name` backfill of existing rows follows the normalizer in SQL. `lower()` follows the database collation, while the Domain uses invariant lower case. Every new or renamed shop gets the Domain value.
- `scansToday` counts invoices created today and does not drop after AD10, which only resets the quota counter.
- A suspension signs members out of every shop they belong to, because refresh tokens are per user.
