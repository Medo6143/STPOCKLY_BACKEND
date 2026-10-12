# ADR-0002: Multi-tenancy with shop members and a global query filter

- Status: Accepted (2026-10-12)
- Date: 2026-10-12

## Context
A shop can have an owner and staff. One user might help in two shops. Rule 5 requires every query to be filtered by the `ShopId` in the JWT.

## Decision
- `shop_members (shop_id, user_id, role)` with roles Owner and Staff.
- The access token carries one active `shop_id` claim and the member `role`. Switching shop issues a new token.
- `ICurrentTenant` reads the claim. `StocklyDbContext` applies `HasQueryFilter(e => e.ShopId == tenant.ShopId)` to every tenant entity, and a `SaveChanges` interceptor stamps and verifies `ShopId` on insert.
- Every child table carries `shop_id` so the filter never requires a join.
- An architecture test fails the build if a tenant entity has no filter.

## Consequences
- Forgetting the filter in a handler is impossible. Wrong-tenant access returns 404, not 403, so IDs cannot be probed.
- `IgnoreQueryFilters()` is allowed only in the Admin feature folder (ADR-0005), checked by an architecture test.

## Update (2026-10-12, step 1.5: identity and tenancy tables)
Approved by the product owner, with a condition.

- **`ShopMember` is not an `ITenantEntity`.** Like `Shop`, it maps users to tenants and must be read at login, before any tenant exists, and `IgnoreQueryFilters()` stays forbidden outside `Application/Features/Admin`.
  **Condition: every query on `shop_members` filters by a user id** (the current user, or the user being logged in or registered). Enforced by construction and proved by tests:
  - All access goes through `Application/Abstractions/Persistence/IShopMembershipRepository`; every read method takes a `Guid userId` (`FindFirstActiveAsync(userId)`, `FindActiveAsync(userId, shopId)`), and the single write takes a `ShopMember` whose `UserId` is mandatory. Its implementation, `Infrastructure/Persistence/Repositories/ShopMembershipRepository`, is the only code that touches the `ShopMember` set; the context has no `ShopMember` `DbSet` property.
  - Architecture test `ShopMemberAccessTests` (source scan of `src/`): `Set<ShopMember>`, a `DbSet<ShopMember>` or a `ShopMembers` member appears only in `ShopMembershipRepository.cs`; the `shop_members` table name appears only there, in `ShopMemberConfiguration.cs` and in migrations; the context exposes no `ShopMember` property.
  - Integration test `ShopMembershipRepositoryTests` (Testcontainers PostgreSQL): two users in two shops; every method returns only rows of the given user id.
- **`Plan` is global** (no tenant filter). **`ShopSubscription` is an `ITenantEntity`** (query filter and stamp interceptor apply). **`RefreshToken` belongs to a user, not a shop** (no tenant filter; looked up only by the hash of a presented token).
- **Registering a shop without a token.** `ITenantScope.BeginForNewShop(shopId)` (Application, scoped per request) opens a scope in which `ICurrentTenant` is the new shop; `HttpContextCurrentTenant` reads an open scope first, then the `shop_id` claim. Scopes do not nest. Only `Application/Features/Auth/Register` may use `ITenantScope`, checked by the architecture test `TenantScopeUsageTests` (source scan, like `IgnoreQueryFiltersTests`).
- **Suspended shops.** The default authorization policy checks the token's shop status on every authenticated request (no tracking) and returns `403 Shop.Suspended`; logout is exempt so a suspended shop can always sign out.
