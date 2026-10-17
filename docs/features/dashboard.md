# Feature: Dashboard

Status: Done · Sprint: 4 · Owner: backend lane

## 1. Purpose
The home screen shows the shop's day at a glance (products, low and negative stock, today's sales, invoices waiting for review) and which received batches expire soon, so the owner acts before stock runs out or expires.

## 2. User flow
1. The owner opens "الرئيسية": the summary cards (D1) load.
2. A tap on "قارب على الانتهاء" opens the list of expiring lines (D2), soonest first, paged.

## 3. Endpoints
| # | Method | Path | Auth / role | Request | Response | Status codes |
|---|---|---|---|---|---|---|
| D1 | GET | `/api/dashboard/summary` | user | none | `200 DashboardSummary` | 200, 401, 403 |
| D2 | GET | `/api/dashboard/expiring-soon` | user | query `days` (default 60, 1..365), `page`, `pageSize` (max 100) | `200 PagedResponse<ExpiringItem>` | 200, 400, 401, 403 |

Code: `DashboardController` → `Application/Features/Dashboard/{GetDashboardSummary,GetExpiringSoon}` → `IDashboardQueries` (`Infrastructure/Persistence/Queries/DashboardQueries`). DTOs: `Contracts/Dashboard`.

## 4. Data model changes
No migration of its own (the sales tables come from migration `Sales`, see sales.md). Queries and the indexes that serve them:

| Figure | Query (tenant filter, `AsNoTracking`) | Index |
|---|---|---|
| `productCount` | `products WHERE NOT is_archived` count | `ix_products_shop_id_normalized_name` (partial `NOT is_archived`) |
| `lowStockCount` | `… AND quantity <= low_stock_threshold` count | `ix_products_low_stock` |
| `negativeStockCount` | `… AND quantity <= low_stock_threshold AND quantity < 0` count (the index predicate is repeated so the partial index serves it; thresholds are >= 0) | `ix_products_low_stock` |
| `todaySales` | `sales WHERE created_at >= @dayStart AND created_at < @dayEnd`: count, sum of `total_amount`, count of sales with a line `unit_price <> list_unit_price` | `ix_sales_shop_id_created_at`; lines by `ux_sale_items_sale_id_line_no` |
| `invoicesAwaitingReview` | `invoices WHERE status = NeedsReview` count | `ix_invoices_shop_id_status_created_at` |
| `expiringSoonCount`, D2 | `invoice_items WHERE confirmed_at IS NOT NULL AND expiry_date <= @until` joined to `products` on `COALESCE(product_id, created_product_id)` `WHERE NOT is_archived AND quantity > 0`; unit factor by primary key; D2 `ORDER BY expiry_date, product name, line id OFFSET … LIMIT …` + count | `ix_invoice_items_shop_id_expiry_date` (partial `expiry_date IS NOT NULL`); products by primary key |

## 5. Integrations
| | |
|---|---|
| Why | Redis read cache for D1 (2 minutes) |
| Endpoint / operation | `GET` version, `GET`/`SET` the summary key |
| Auth | Redis password from `.env` |
| Timeout / retry | 250 ms, no retry |
| Failure behaviour | version unreadable → cache skipped, numbers from PostgreSQL (rule 25) |
| Cost | none |

## 6. Cache keys
| Key | TTL | Invalidated by |
|---|---|---|
| `stockly:{shopId}:dashboard:summary:v{ver}:{yyyyMMdd}` | 2 min | catalog version bump (every product, unit, stock, confirm and sale write, after commit); the Cairo date in the key ends a value at local midnight |

D2 is not cached (paged, opened rarely).

## 7. Business rules
- "Today" is the Africa/Cairo calendar day from `IClock` (`BusinessCalendar.CairoDayRangeUtc`: start inclusive, next start exclusive, DST-safe), never `DateTime.Now`.
- Low stock = `quantity <= low_stock_threshold` (same as P1), so negative and empty products are low; archived products are never counted.
- `overriddenSalesCount` = today's sales with at least one price-overridden line.
- `invoicesAwaitingReview` = invoices in `NeedsReview`. A scan or job does not bump the catalog version, so this figure can be up to 2 minutes old.
- Expiring (ADR-0004, a hint): lines of **confirmed** invoices whose `expiry_date` is on or before today (Cairo) + `days`, whose product is not archived and still has stock. Lines already past their date are included (still on the shelf). D1 uses the default window of 60 days. A new-product line names the product created from it.
- `lineQuantity` is the line's quantity in the product's base unit (`round(quantity × factor, 3)`, as `ProductUnit.ToBase`); `currentQuantity` is the product's quantity now.
- D2 is sorted by expiry date, then product name, then line id (stable paging).

## 8. Error cases
| Case | Error code | HTTP | User message (ar / en) |
|---|---|---|---|
| `days` outside 1..365, `page < 1`, `pageSize` outside 1..100 | `Validation.Failed` | 400 | حقل واحد أو أكثر غير صالح. / One or more fields are invalid. |

## 9. Configuration / env vars
None.

## 10. Mobile / Admin screens
"الرئيسية" cards and "قارب على الانتهاء" list, each with loading, empty, error and data states; RTL first. Admin: none.

## 11. Tests
| Project / class | Covers |
|---|---|
| `Application.Tests` `Features/Dashboard/DashboardAndLookupHandlerTests` | Cache miss → counts for the Cairo day (after Cairo midnight, still 17 Oct in UTC) and set with version and date, 2 minutes; hit → no PostgreSQL; version bump → new key; Redis down → no cache; `CairoDayRangeUtc` either side of midnight; D2 window end and validator |
| `Api.IntegrationTests` `Sales/LookupAndDashboardEndpointTests` | D1 counts (archived excluded, low/negative, today's sales total and overridden, awaiting review, expiring); cache hit until a sale bumps the version; Redis down; D2 window, confirmed only, stock filter, base-unit line quantity, new product line, order, paging, `days` validation |

## 12. Known limitations
- `invoicesAwaitingReview` may lag up to 2 minutes after a scan completes (cache TTL; scans do not bump the catalog version).
- Expiry is per invoice line, not per remaining unit (ADR-0004): a line stays listed until its product's stock reaches zero.
