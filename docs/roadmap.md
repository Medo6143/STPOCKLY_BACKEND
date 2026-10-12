# Stockly — Roadmap

Status: **Draft for review** · Updated 2026-10-12

Each sprint ends with a demo and a working build from a clean `docker compose up -d`. A sprint item is done only when it meets the Definition of Done (bottom of this file).

---

## Sprint 0 — Planning (now)

| Item | Status |
|---|---|
| Product decisions (units, sales, expiry, members, offline, admin) | Done |
| ERD with indexes and constraints (`docs/erd.md`) | Approved |
| ADRs 0001–0009 | Approved |
| Redis design (`docs/design/redis.md`) | Approved |
| `AGENTS.md`, structure, roadmap | Approved |
| Brand: logo, splash animation, tokens (`brand/`, now in `stockly-mobile`, ADR-0011) | Approved |
| Backend system design (`docs/design/backend.md`) | Before Sprint 2 |
| Frontend system design: admin here, mobile in `stockly-mobile` (`docs/design/frontend.md`) | Before Sprint 3 |

**Exit:** all of the above approved, except the two system design docs. `docs/design/backend.md` is written and approved before Sprint 2 starts, `docs/design/frontend.md` before Sprint 3 (see `docs/prompts/execution-prompt.md`, Part D).

---

## Sprint 1 — Foundation, Auth, Products

**Goal:** a shop owner can register and manage products with units through the API.

- Solution with Domain, Contracts, Application, Infrastructure, Api + 4 test projects, `Directory.Build.props`, central packages
- Architecture tests (layer rules, tenant filter rule)
- `docker-compose.yml` (postgres, redis, minio, pgadmin) + `.env.example`, health checks
- Domain: `Product`, `ProductUnit`, `StockMovement` rules, `ArabicTextNormalizer`, `Result`/`Error` with unit tests
- `StocklyDbContext`, tenant global filter + interceptor, first migration with all Sprint 1 indexes
- Auth: register (creates user + shop + Owner membership + default plan), login, refresh with rotation, logout
- Products: create, update, archive, list (paged, search), get, units CRUD, manual adjust, stock count
- Redis: `ICacheService` + version keys for product list
- ProblemDetails middleware, Serilog, correlation id, `/health/live` and `/health/ready`
- Docs: `features/auth.md`, `features/products.md`

## Sprint 2 — AI scan pipeline

**Goal:** a photo uploaded to the API becomes a reviewed-ready invoice.

- `IFileStorage` + MinIO
- `POST /invoices/scan`: validation (type, 8 MB max), `ClientRequestId` idempotency, quota + rate limit, 202
- Hangfire `ProcessInvoiceJob`: storage → AI cache → OpenRouter → validate → match → save
- `OpenRouterInvoiceExtractor` with Polly, fallback model, full fake-handler test suite
- Validation rules: item cap, totals tolerance 2%, unreadable lines, duplicate invoice number
- Product matching v1: exact alias, exact normalized name
- `GET /invoices/{id}` with status cache, `PUT /invoices/{id}/items`
- `AiUsageRecord` on every call
- Docs: `features/invoice-scan.md` (full integration section for OpenRouter, Hangfire, MinIO, Redis)

## Sprint 3 — Mobile foundation and scan flow

_Built in the `stockly-mobile` repository (ADR-0011) against `docs/api/openapi.json` from this repository._

**Goal:** the owner uses the real app to log in, see products and scan an invoice.

- Flutter project, theme tokens from `brand/`, light/dark, `ar`/`en` with RTL, go_router, Dio with refresh interceptor
- Native splash + animated splash
- Auth screens, product list/detail/edit with units
- Camera capture + compression, offline queue (Drift) + background sync
- Processing screen (polling), review screen (edit lines, choose product/unit, add new product)
- Widget tests for 4 states × 2 languages on every screen
- Docs: update feature docs with mobile screens

## Sprint 4 — Confirm, sales, dashboard

**Goal:** stock is correct end to end, in and out.

- `POST /invoices/{id}/confirm`: transaction, row locks, idempotent, creates products/aliases, cache bump. Concurrency test with two parallel confirms.
- Quick sale: `POST /sales` (idempotent), barcode lookup, mobile quick-sale screen
- Low-stock list, expiring-soon card, negative-stock warning
- Dashboard summary (backend + mobile), cached 2 min
- Docs: `features/confirm-invoice.md`, `features/sales.md`, `features/dashboard.md`

## Sprint 5 — Admin dashboard (Blazor)

**Goal:** the platform owner can run the business.

- `Stockly.Admin` (Blazor WASM + MudBlazor), admin login
- Shops: list, detail, suspend/activate, members
- Plans and subscriptions, quota override
- AI usage and cost per shop and per day, model breakdown
- Failed scans with image (pre-signed URL) and raw AI JSON, re-run job
- Audit log, Hangfire dashboard behind admin policy
- Docs: `features/admin.md`

## Sprint 6 — Smart matching, CI, hardening

**Goal:** ready for first real shops.

- Fuzzy matching with `pg_trgm` on aliases and names ("بنادول اكسترا" ↔ "Panadol Extra"), confidence shown on review
- GitHub Actions: backend and admin workflows here, mobile workflow in `stockly-mobile` (ADR-0011); coverage gate 80% on Domain/Application
- Load test: polling and scan endpoint; security review (OWASP API Top 10 checklist)
- Image lifecycle (90 days), backup plan for Postgres
- Release builds (Android first), privacy policy text

---

## Later (not scheduled)

Lot tracking with FEFO (ADR-0004 follow-up), returns to supplier, reports export (Excel/PDF), multiple branches per shop, POS/barcode printer integration, WhatsApp low-stock alerts, iOS release.

---

## Definition of Done (every item)

1. Code follows `AGENTS.md`, review checklist complete.
2. Tests written first or alongside; build, tests, analyze green with zero warnings.
3. Feature doc created/updated; ERD/Redis docs updated if indexes or keys changed.
4. Migration reviewed.
5. Mobile items checked in Arabic + English and light + dark.
6. Demoed from a clean `docker compose up -d`.

## Assumptions used for this plan (change if wrong)

- Launch with **one plan** that has a daily scan quota. The `plans` table already supports more tiers later.
- **Owner and Staff** can scan, review, confirm and sell. Only the **Owner** can adjust stock, run a stock count, archive products and manage members.
