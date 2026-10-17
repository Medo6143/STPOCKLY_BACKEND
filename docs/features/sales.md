# Feature: Sales

Status: Done · Sprint: 4 · Owner: backend lane

## 1. Purpose
The quick-sale screen records what the shop sold so stock goes down. A sale never blocks on missing stock (a sale before its invoice is real life); negative stock is reported as a warning and on the dashboard. Retries from a flaky connection never create a second sale.

## 2. User flow
1. The cashier opens "بيع سريع", scans barcodes (P12) or picks products, chooses the unit and quantity, and optionally changes the price.
2. The app creates a `clientRequestId` once for this sale and sends S1; it resends the same id on retry.
3. The app shows the sale and, if any, "المخزون سالب" for the products whose stock went below zero.
4. "المبيعات" lists past sales (S2); a tap opens one (S3).

## 3. Endpoints
| # | Method | Path | Auth / role | Request | Response | Status codes |
|---|---|---|---|---|---|---|
| S1 | POST | `/api/sales` | user | `CreateSaleRequest { clientRequestId, note?, items[1..100] { productId, productUnitId, quantity, unitPrice? } }` | new: `201 SaleDto`, `Location: /api/sales/{id}`; repeat `clientRequestId`: `200 SaleDto` | 200, 201, 400, 401, 403, 404 |
| S2 | GET | `/api/sales` | user | query `from`, `to` (optional UTC timestamps), `page`, `pageSize` (max 100) | `200 PagedResponse<SaleListItem>`, newest first | 200, 400, 401, 403 |
| S3 | GET | `/api/sales/{id}` | user | none | `200 SaleDto` | 200, 401, 403, 404 |

Code: `SalesController` → `Application/Features/Sales/{CreateSale,GetSales,GetSale}` → `ISaleRepository`, `ISaleQueries`, `IStockService`. DTOs: `Contracts/Sales`. S1 is not wrapped in `TransactionBehavior`: the handler owns its transaction (repeat answered after the rollback; version bump after the commit).

## 4. Data model changes
Migration **`Sales`** (`20261017202302_Sales`), erd §3.4:

| Object | Definition | Serves |
|---|---|---|
| `sales` | `id`, `shop_id`, `client_request_id`, `created_by_user_id` (FK users), `total_amount numeric(18,2)`, `note text NULL`, `created_at`; `CHECK (total_amount >= 0)` | |
| `sale_items` | `id`, `shop_id`, `sale_id` (FK), `line_no int`, `product_id` (FK), `product_unit_id` (FK), `quantity numeric(14,3)`, `list_unit_price`, `unit_price`, `line_total numeric(18,2)`; `CHECK (quantity > 0 AND list_unit_price >= 0 AND unit_price >= 0 AND line_total >= 0)`, `CHECK (line_no BETWEEN 1 AND 100)` | |
| `ux_sales_shop_id_client_request_id` | `UNIQUE (shop_id, client_request_id)` | S1 repeat lookup; serializes concurrent repeats |
| `ix_sales_shop_id_created_at` | `(shop_id, created_at DESC)` | S2 (`WHERE shop_id = @shop [AND created_at >= @from] [AND created_at < @to] ORDER BY created_at DESC, id DESC OFFSET … LIMIT …` + count); D1 today's sales |
| `ux_sale_items_sale_id_line_no` | `UNIQUE (sale_id, line_no)` | loading a sale's lines in order; S2 item count; FK support |
| `ix_sale_items_shop_id_product_id` | `(shop_id, product_id)` | top sellers (erd §3.4) |
| FKs | `stock_movements.sale_id → sales`, `stock_movements.sale_item_id → sale_items` (restrict; deferred since InitialCatalog) | |

EF also creates FK-support indexes `ix_sales_created_by_user_id`, `ix_sale_items_product_id`, `ix_sale_items_product_unit_id`, `ix_stock_movements_sale_id` (no named query). `line_no` is an addition to the ERD draft: lines keep request order (UUIDv7 ids do not order inside one millisecond).

## 5. Integrations
| | |
|---|---|
| Why | Redis catalog version (product lists and dashboard) |
| Endpoint / operation | `INCR stockly:{shopId}:ver:catalog` after the commit of a new sale |
| Auth | Redis password from `.env` |
| Timeout / retry | 250 ms, no retry |
| Failure behaviour | warning only; the sale is recorded (rule 25) |
| Cost | none |

## 6. Cache keys
| Key | TTL | Invalidated by |
|---|---|---|
| `stockly:{shopId}:ver:catalog` | none | bumped after a new sale commits (not on a repeat or a failure) |

Sales themselves are not cached.

## 7. Business rules
- Transaction (rules 1, 3, 4, 24): look up `clientRequestId` → existing sale `200`; else in one transaction **insert the sale header first** (a concurrent request with the same id blocks on `ux_sales_shop_id_client_request_id` until the first commits, then loses and returns the winner with `200`), then lock the products with the same ordered statement as confirm (`SELECT … WHERE id = ANY(@ids) ORDER BY id FOR UPDATE`), then add each line and a `Sale` movement through `IStockService` (`-ToBase(quantity)`, rule 5), save, return the projection.
- Lines are processed in request order; the first failing line decides the error and the whole sale rolls back.
- `unitPrice` absent → the unit's `sellPrice`. Each line stores `list_unit_price` (the unit's price at sale time) and `unit_price` (charged); `priceOverridden` per line when they differ, per sale when any line does.
- `line_total = round(quantity × unit_price, 2)` away from zero (backend.md §17.1 item 6); `total_amount` = sum of line totals.
- Negative stock never blocks. `warnings` has one `{ code: "Stock.NegativeBalance", productId }` per product whose `balance_after` after its last line of this sale is below zero. The same warnings come back on a repeat and on S3 (computed from the stored movements).
- `balanceAfter` per line = that line's movement `balance_after` (`ux_stock_movements_sale_item_id`). Two lines of one product chain.
- Archived product → `400 Sale.ArchivedProduct`. Unit of another product of the shop → `400 Sale.UnitNotOfProduct`; unit unknown in the shop → `404 ProductUnit.NotFound`. Product missing or another shop's → `404 Product.NotFound` (rule 10).
- S2: `from` inclusive, `to` exclusive, compared in UTC; `to < from` → `400 Validation.Failed`. S3: missing or another shop's → `404 Sale.NotFound`.

## 8. Error cases
| Case | Error code | HTTP | User message (ar / en) |
|---|---|---|---|
| Shape: no/empty `clientRequestId`, 0 or > 100 items, quantity not > 0, malformed decimal, JSON number, note > 500, `pageSize > 100`, `to < from` | `Validation.Failed` | 400 | حقل واحد أو أكثر غير صالح. / One or more fields are invalid. |
| Too many decimals, negative price, out of range | `Precision.*` | 400 | see products.md |
| Archived product | `Sale.ArchivedProduct` | 400 | لا يمكن بيع منتج مؤرشف. / An archived product cannot be sold. |
| Unit of another product | `Sale.UnitNotOfProduct` | 400 | الوحدة المختارة تخص منتجاً آخر. / The chosen unit belongs to another product. |
| Product missing / another shop's | `Product.NotFound` | 404 | المنتج غير موجود في هذا المتجر. / The product was not found in this shop. |
| Unit missing | `ProductUnit.NotFound` | 404 | الوحدة غير موجودة في هذا المنتج. / The unit was not found on this product. |
| Sale missing / another shop's (S3) | `Sale.NotFound` | 404 | لم يتم العثور على عملية البيع. / The sale was not found. |

Internal only: `Sale.DuplicateClientRequest` (lost race, answered with `200`), and the Domain guards `Sale.ShopRequired`, `Sale.ClientRequestIdRequired`, `Sale.UserRequired`, `Sale.NoteTooLong`, `Sale.TooManyItems`, `Sale.QuantityNotPositive`.

## 9. Configuration / env vars
None.

## 10. Mobile / Admin screens
"بيع سريع" (cart, barcode scan, price override, submit with loading/error, negative-stock warning) and "المبيعات" (list with loading, empty, error, data; detail). RTL first; decimals with the `decimal` package. Admin: none.

## 11. Tests
| Project / class | Covers |
|---|---|
| `Domain.Tests` `Sales/SaleTests` | Header validation and trimmed note; default and overridden price; rounding away from zero; archived product; unit of another product; bad quantity / price; 100-line cap |
| `Application.Tests` `Features/Sales/CreateSaleCommandHandlerTests` | Header saved before lines, base-unit movements, bump after commit; repeat without a transaction; lost race → winner, no bump; product not found / other shop; unit errors; archived; precision; unauthenticated; validator shape; S3 not found; S2 range in UTC and validator |
| `Infrastructure.Tests` `Persistence/Sprint4PersistenceTests` | Sale round trip with both prices and rounded total; unique client request id; `ck_sale_items_amounts` |
| `Infrastructure.Tests` `Persistence/MigrationTests` | New tables, checks, indexes and the two now-real FKs |
| `Api.IntegrationTests` `Sales/SalesEndpointTests` | 201 with prices, totals, `balanceAfter`, warning and `Location`; S3 equals S1; repeat `200`; **4 parallel requests with one `clientRequestId` → one sale**; **6 parallel sales of one product → chained balances**; **confirm and sale on overlapping products in parallel, 5 rounds → no deadlock**; archived (whole sale rolled back); unit errors and other shop; shape and precision; S2 paging, range and validation; S3 wrong tenant; Redis down |

## 12. Known limitations
- No sale cancellation or return yet (a correction is a manual adjustment).
- No "top sellers" endpoint yet; its index exists.
