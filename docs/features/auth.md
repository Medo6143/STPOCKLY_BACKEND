# Feature: Auth (register, login, refresh, logout, session restore)

Status: Done (backend, step 1.5) · Sprint: 1 · Owner: medo6143

## 1. Purpose
A shop owner creates an account and their shop in one step, signs in on any phone, and stays signed in without typing the password again. Every later endpoint relies on the access token this feature issues: its `shop_id` claim is the tenant (ADR-0002) and its `role` claim drives owner-only rules. A stolen refresh token is detected on its first reuse and the stolen device session is ended.

## 2. User flow
1. **إنشاء حساب** (Create account): the owner enters name, email, password, optional phone and language, and the shop (name, type, governorate). The app calls A1 and stores the token pair in secure storage.
2. **تسجيل الدخول** (Sign in): email and password on a new device (A2). Each sign-in is its own device session (refresh token family).
3. App start-up: the app calls A5 with the stored access token to restore the session (user, shop, role, language).
4. Any `401` from an authenticated endpoint: the app calls A3 once, retries the original request once, and signs out if the refresh fails (contract §1.4).
5. **تسجيل الخروج** (Sign out): A4 ends this device session; other devices stay signed in. It works for suspended shops too.

## 3. Endpoints
Contract v1.2 rows A1–A5 (`docs/api/contract.md` §4); machine-readable in `docs/api/openapi.json`.

| Method | Path | Auth / role | Request | Response | Status codes |
|---|---|---|---|---|---|
| POST | `/api/auth/register` | anonymous | `RegisterRequest` | `201 AuthResponse` (role Owner), `Location: /api/auth/me` | 201, 400, 409 |
| POST | `/api/auth/login` | anonymous | `LoginRequest` | `200 AuthResponse` | 200, 400, 401, 403 |
| POST | `/api/auth/refresh` | anonymous | `RefreshTokenRequest` | `200 AuthResponse` with a new refresh token | 200, 400, 401, 403 |
| POST | `/api/auth/logout` | user (also suspended shops) | `RefreshTokenRequest` | `204` | 204, 400, 401 |
| GET | `/api/auth/me` | user | none | `200 MeResponse` | 200, 401, 403 |

Code: `Api/Controllers/AuthController` → MediatR → `Application/Features/Auth/{Register,Login,Refresh,Logout,GetMe}`. DTOs: `Contracts/Auth`, enums `Contracts/Shops` (`ShopType`, `ShopStatus`, `ShopRole`, serialized as PascalCase names).

## 4. Data model changes
Migration **`Identity`** (`20261012184800_Identity`). Full index list and the query each serves: `docs/erd.md` §3.1; migration status: §3.7.

| Table | Notes |
|---|---|
| `users` | ASP.NET Core Identity user (`IdentityUserContext<ApplicationUser, Guid>`, no roles tables) remapped to snake_case. Stockly columns: `full_name`, `preferred_language` (smallint: Ar 1, En 2), `platform_role` (smallint: None 0, PlatformAdmin 1), `created_at`, `last_login_at`; Identity's `PhoneNumber` is the `phone` column. `UNIQUE (normalized_email)` (login, case-insensitive unique email) and Identity's `UNIQUE (normalized_user_name)` (the user name is the email). |
| `user_claims`, `user_logins`, `user_tokens` | Identity's user store tables; unused by Stockly today. |
| `refresh_tokens` | `token_hash` (lower-case hex SHA-256), `family_id` (id of the family's first token), `device_id`, `expires_at`, `revoked_at`, `replaced_by_id`, `created_at`. `UNIQUE (token_hash)`; `(user_id) WHERE revoked_at IS NULL`; `(family_id) WHERE revoked_at IS NULL`. FK `user_id` → `users` (cascade). |
| `shop_members` | PK `(shop_id, user_id)`, `role` (smallint: Owner 1, Staff 2), `is_active`, `joined_at`. `(user_id) WHERE is_active`; `UNIQUE (shop_id) WHERE role = 1` (one owner per shop). |
| `plans` | `UNIQUE (code)`. Seeded with the default plan (section 7, "Assumptions"). |
| `shop_subscriptions` | Tenant table. `status` (smallint: Active 1, Ended 2), `starts_on`, `ends_on` (date, NULL = open-ended). `UNIQUE (shop_id) WHERE status = 1`; `(ends_on) WHERE status = 1`. |
| `stock_movements` | The deferred FK `created_by_user_id` → `users` is now real (restrict). |

Domain types: `Shops/ShopMember`, `ShopRole`, `Plan`, `DefaultPlan`, `ShopSubscription`, `SubscriptionStatus`; `Users/RefreshToken`, `PreferredLanguage`, `PlatformRole`. `ApplicationUser` lives in `Infrastructure/Identity` (it derives from an Identity type); Application sees users only through `IUserAccounts`.

Tenancy (ADR-0002, update 2026-10-12): `ShopMember` is **not** a tenant entity (read at login before any tenant exists); every query on it filters by user id, enforced by `IShopMembershipRepository` + architecture and integration tests. `Plan` is global. `ShopSubscription` is a tenant entity. `RefreshToken` belongs to a user, not a shop.

## 5. Integrations
| | PostgreSQL (ASP.NET Core Identity store) |
|---|---|
| Why | Users, password hashes (Identity's PBKDF2 hasher), tokens and memberships |
| Endpoint / operation | `StocklyDbContext` (also the Identity user store), one transaction per command through `IUnitOfWork` inside the Npgsql execution strategy |
| Auth | Connection string from `.env` / environment |
| Timeout / retry | Npgsql defaults; no retry strategy configured |
| Failure behaviour | Database down → `500 Server.Unexpected` |
| Cost | One indexed lookup of the token's shop status per authenticated request |

No Redis, Hangfire, MinIO or OpenRouter in this feature.

## 6. Cache keys
None. The suspended-shop check reads `shops.status` from PostgreSQL on every authenticated request (no tracking, primary key lookup). `docs/design/redis.md` is unchanged.

## 7. Business rules
**Register (A1)**
- One transaction: user, shop, Owner membership, active subscription to the default plan, first refresh token family. Any failure rolls everything back (a duplicate email leaves no shop behind).
- The new shop has no token yet, so its tenant rows are written inside `ITenantScope.BeginForNewShop(shopId)`; while the scope is open `ICurrentTenant` is that shop. Only `Features/Auth/Register` may use `ITenantScope` (architecture test).
- Email: required, valid, max 256, unique case-insensitively (`normalized_email`). Password: 8–128 characters, at least one letter (any script) and one ASCII digit 0-9 (Identity's `RequireDigit` counts only 0-9). Full name required, max 200. Phone max 30. `preferredLanguage` `ar` (default) or `en`. Device id max 100. Shop name required, max 200, non-empty after Arabic normalization (`Shop.NameRequired`); shop type exactly one of `Pharmacy`, `Grocery`, `MobileShop`, `Other` (`Shop.UnknownType`; numbers and other casings rejected).
- Subscription `starts_on` = today in Africa/Cairo (`BusinessCalendar.Today(IClock)`), `ends_on` NULL.

**Login (A2)**
- Unknown email and wrong password give the same `401 Auth.InvalidCredentials`; an unknown email still costs one password hash.
- The token is for the active membership joined first (ties broken by shop id). No active membership → `403 Auth.NoActiveShop`. Suspended shop → `403 Shop.Suspended`.
- Each login starts a new refresh token family (one device session) and records `last_login_at`.

**Tokens**
- Access token: JWT HS256 signed with `Jwt:SigningKey`, issuer `Jwt:Issuer` (default `stockly`), audience `Jwt:Audience` (default `stockly-app`), 15 minutes, no clock skew. Claims `sub`, `shop_id`, `role` (`Owner`/`Staff`), `jti`. Issued at and expiry come from `IClock`, truncated to whole seconds, so `accessTokenExpiresAt` equals `exp`.
- Refresh token: 32 random bytes, base64url (43 characters), 30 days. Only its SHA-256 hash is stored. Expiry timestamps are UTC with `Z`.

**Refresh (A3)**
- The presented token's row is locked (`SELECT … FOR UPDATE`), revoked, and its successor is inserted in the same family with `replaced_by_id` chaining.
- A revoked token → the **whole family** is revoked, including its active token, and `401 Auth.RefreshTokenReused`. This revocation is committed although the command fails (`ICommitOnFailure`). Other families (other devices) of the same user are unaffected.
- Unknown or expired token → `401 Auth.InvalidRefreshToken` (nothing revoked). User or active membership gone → `401 Auth.InvalidRefreshToken`. Suspended shop → `403 Shop.Suspended` (token not rotated).
- The shop is resolved like at login (active membership joined first).

**Logout (A4)**
- Revokes the presented token's whole family, only if it belongs to the caller. Unknown, already revoked or another user's token → still `204`, nothing revealed.
- Allowed for suspended shops (policy `AllowSuspendedShop`).

**Session restore (A5) and every authenticated request**
- `[Authorize]` (default policy) = valid access token + the token's shop is not suspended (`ActiveShopRequirement`, checked per request). Suspended → `403 Shop.Suspended`. Policy `Owner` adds role Owner (used from step 1.6); a Staff token gets `403 Auth.Forbidden`.
- A5 returns the user and the token's shop with the user's role. A token whose user or active membership in that shop no longer exists → `401 Auth.Unauthenticated` (the client refreshes or signs out).

**Errors and language**
- Every error is RFC 7807 with a stable `code` (contract §1.8); `title`/`detail` are localized by code from `Api/Resources/ErrorMessages(.ar).resx`; request-shape errors are `400 Validation.Failed` with `errors` keyed by camelCase field paths (`shop.name`). `Accept-Language` selects `ar` (default) or `en` (regional variants fall back: `ar-EG` → `ar`); codes never change. Formatting culture stays invariant.

### Assumptions (product owner)
- **Default plan** (seeded by migration `Identity`, values in one place: `Domain/Shops/DefaultPlan.cs`): code `standard`, `name_ar` "الأساسية", `name_en` "Standard", **30** AI scans per shop per Cairo day (AI scans cost money; to be raised later), **3** members including the owner, monthly price **0.00** EGP, active. Changing a value is a one-line change there plus a new migration.
- Every registration subscribes the new shop to that plan with no end date.

## 8. Error cases
| Case | Error code | HTTP | User message (ar / en) |
|---|---|---|---|
| Request shape invalid (missing field, bad email, weak password, malformed JSON) | `Validation.Failed` (+ `errors`) | 400 | حقل واحد أو أكثر غير صالح. راجع 'errors'. / One or more fields are invalid. See 'errors'. |
| Shop name blank after normalization | `Shop.NameRequired` | 400 | اسم المتجر مطلوب. / Shop name is required. |
| Unsupported shop type | `Shop.UnknownType` | 400 | نوع المتجر غير مدعوم. / The shop type is not supported. |
| Email already registered | `Auth.EmailTaken` | 409 | هذا البريد الإلكتروني مسجل بالفعل. / This email is already registered. |
| Unknown email or wrong password | `Auth.InvalidCredentials` | 401 | البريد الإلكتروني أو كلمة المرور غير صحيحة. / The email or password is incorrect. |
| No active membership | `Auth.NoActiveShop` | 403 | حسابك ليس عضواً نشطاً في أي متجر. / Your account is not an active member of any shop. |
| Refresh token unknown or expired | `Auth.InvalidRefreshToken` | 401 | انتهت جلستك. يرجى تسجيل الدخول مرة أخرى. / Your session has expired. Please sign in again. |
| Revoked refresh token reused | `Auth.RefreshTokenReused` | 401 | تم إنهاء هذه الجلسة لأسباب أمنية. يرجى تسجيل الدخول مرة أخرى. / This session was ended for security reasons. Please sign in again. |
| Missing, invalid or expired access token | `Auth.Unauthenticated` | 401 | رمز الدخول مفقود أو غير صالح أو منتهي الصلاحية. يرجى تسجيل الدخول مرة أخرى. / The access token is missing, invalid or expired. Please sign in again. |
| Role not allowed (owner-only) | `Auth.Forbidden` | 403 | دورك في المتجر لا يسمح بهذا الإجراء. / Your role does not allow this action. |
| Shop suspended | `Shop.Suspended` | 403 | هذا المتجر موقوف. يرجى التواصل مع دعم Stockly. / This shop is suspended. Please contact Stockly support. |
| Unknown route / wrong method / wrong content type | `Http.NotFound` / `Http.MethodNotAllowed` / `Http.UnsupportedMediaType` | 404 / 405 / 415 | localized, see `ErrorMessages*.resx` |
| Unexpected | `Server.Unexpected` | 500 | حدث خطأ غير متوقع. / An unexpected error occurred. |

## 9. Configuration / env vars
| Name | Example | Secret? |
|---|---|---|
| `Jwt__SigningKey` | 32+ bytes of random data (`openssl rand -base64 48`) | **Yes**. Required: startup fails naming the variable when missing or shorter than 32 bytes; never logged or echoed. |
| `Jwt__Issuer` | `stockly` (default) | No |
| `Jwt__Audience` | `stockly-app` (default) | No |

## 10. Mobile / Admin screens
Mobile (stockly-mobile): register, sign-in and splash/session-restore screens with the 4 states, RTL first; tokens in `flutter_secure_storage` only; one refresh + one retry on `401`, sharing one refresh across concurrent `401`s. Admin: Sprint 5.

## 11. Tests
| Project / class | Covers |
|---|---|
| `Domain.Tests` `Shops/ShopMemberTests`, `PlanTests`, `ShopSubscriptionTests`, `Users/RefreshTokenTests`, `Common/ErrorTests` | Invariants; default plan values valid; family start, rotation chain, expiry; enum values stable |
| `Application.Tests` `Features/Auth/*HandlerTests` | Register, Login, Refresh, Logout, GetMe with NSubstitute: happy path, validation/domain failures, not found / invalid token, suspended shop, no active shop, wrong user/tenant (another user's token, a shop the user is not a member of) |
| `Application.Tests` `AuthValidatorTests`, `AuthMappingTests`, `Behaviors/PipelineBehaviorTests` | Contract limits, password policy, localized custom messages; enum alignment Domain ↔ Contracts; Cairo date; ValidationBehavior (`Validation.Failed`, camelCase paths), TransactionBehavior (commit, rollback, commit-on-failure) |
| `Infrastructure.Tests` (Testcontainers PostgreSQL) `MigrationTests`, `IdentityConstraintTests` | Every erd §3.1 index and partial predicate, default plan seeded, FK `created_by_user_id`, no pending model changes; one owner, duplicate hash/email/code, one active subscription, FK violation |
| `Infrastructure.Tests` `ShopMembershipRepositoryTests` | Two users in two shops: every method returns only the given user's rows; first-joined ordering; inactive excluded |
| `Infrastructure.Tests` `RefreshTokenRepositoryTests`, `UnitOfWorkTests`, `ShopAndPlanRepositoryTests`, `Identity/IdentityUserAccountsTests`, `Identity/JwtTokenServiceTests` | Rotation chain persisted, family revoke leaves other families, `FOR UPDATE` lock; commit/rollback; subscriptions tenant-filtered and not insertable without a tenant; Identity create/duplicate (also concurrent)/credentials; token claims, lifetime, refresh format and hash |
| `Api.IntegrationTests` (Testcontainers + `WebApplicationFactory`) `Auth/*EndpointTests`, `ErrorConventionTests`, `Errors/ErrorTextTests`, `OpenApiShapeTests` | register → me → refresh → reuse → logout; device A/B family test; duplicate email 409; invalid credentials 401; garbage/expired/foreign-key/foreign-issuer token 401; suspended shop 403 on login, refresh, me and 204 on logout; Owner policy; `Accept-Language` en vs ar; validation 400 with `errors`; malformed JSON; 404/405/415 `Http.*`; every code has ar and en text |
| `ArchitectureTests` `TenantScopeUsageTests`, `ShopMemberAccessTests` | `ITenantScope` only in `Features/Auth/Register`; `ShopMember` set only in `ShopMembershipRepository`, `shop_members` only in its mapping, repository and migrations; no `ShopMember` DbSet property |

Coverage at the end of step 1.5: Application 95.5% lines, Domain 98.8% lines.

## 12. Known limitations
- No account lockout and no login rate limiting yet (Identity lockout is not enabled). Tracked for the rate-limiting step.
- No email confirmation, password reset or change password in v1.
- No shop switching (contract §10.6); the token is always for the active membership joined first.
- A user whose membership is deactivated keeps a valid access token for up to 15 minutes; refresh and `/me` then fail with `401`.
- "Logout everywhere" is not an endpoint yet (the `(user_id) WHERE revoked_at IS NULL` index is ready for it).
