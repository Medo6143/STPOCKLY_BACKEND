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
