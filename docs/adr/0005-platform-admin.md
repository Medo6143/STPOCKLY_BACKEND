# ADR-0005: Platform admin dashboard (Blazor WASM) and the tenant-filter exception

- Status: Accepted (2026-10-12)
- Date: 2026-10-12

## Context
The platform owner needs to manage shops, plans, quotas, and watch AI cost and failed scans across all shops.

## Decision
- New project `Stockly.Admin`: Blazor WebAssembly + MudBlazor, served as static files. It calls the API with JWT like the mobile app. It never references Infrastructure or the database.
- Users with `platform_role = PlatformAdmin` get an admin token (no `shop_id` claim). Admin endpoints live under `/api/admin/*`, protected by policy `PlatformAdmin`.
- Admin handlers live in `Application/Features/Admin` and are the only place allowed to call `IgnoreQueryFilters()`. Enforced by an architecture test.
- Every admin write is recorded in `admin_audit_logs`.
- Hangfire dashboard is exposed only to PlatformAdmin.

## Consequences
- Rule 5 has one documented, test-enforced exception.
- Admin and mobile share the same DTO contracts. A shared `Stockly.Contracts` project is created so Blazor can reference request/response types without referencing Application.
