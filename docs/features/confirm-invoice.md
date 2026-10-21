# Feature: Confirm invoice

Status: Done · Sprint: 4 · Owner: backend lane

## 1. Purpose
After the owner has reviewed a scanned invoice (I4), confirming it receives the stock of every line, creates the products the owner added as new, records the supplier and teaches the matcher how this supplier names each product. It is the only place an invoice changes stock, and only on a person's request (AGENTS.md rule 19).

## 2. User flow
1. The owner opens a reviewed invoice ("مراجعة الفاتورة") with every line mapped to a product or a new product.
2. They tap "تأكيد الفاتورة". The app calls I5.
3. The invoice is shown as confirmed; product quantities, the dashboard and the low-stock list reflect it. A retry (lost response, double tap) returns the same confirmed invoice.

## 3. Endpoints
| # | Method | Path | Auth / role | Request | Response | Status codes |
|---|---|---|---|---|---|---|
| I5 | POST | `/api/invoices/{id}/confirm` | user | none | `200 InvoiceDetail` (status `Confirmed`) | 200, 400, 401, 403, 404, 409 |

Code: `InvoicesController.ConfirmAsync` → `Application/Features/Invoices/ConfirmInvoice/{ConfirmInvoiceCommand,ConfirmInvoiceCommandHandler}`. The command is **not** wrapped in `TransactionBehavior`: the handler runs `IUnitOfWork.ExecuteInTransactionAsync` itself so the cache work happens strictly after the commit and the last-safety-net conflict can be answered after the rollback. A confirmed new-product line returns the created product's `productId`/`productName` (backend.md §9.2); `newProduct` is kept. The two pure rules live in the Domain, so the handler only orchestrates (locks, receipts, save): `Domain/Invoices/NewProductsFromInvoice` (which products a confirm creates, §10.5) and `Domain/Invoices/InvoiceAliasLearning` (which aliases it learns, §11 step 8), tested in `Domain.Tests` `Invoices/InvoiceConfirmationRulesTests`.

## 4. Data model changes
No new table or index for confirm (migration `InvoiceScan` already has every column and index). The transaction, docs/design/backend.md §11:

| Step | Statement | Index |
|---|---|---|
| 1 | `SELECT 1 FROM invoices WHERE id = @id AND shop_id = @shop FOR UPDATE`, then load the invoice with its lines | primary key, `ux_invoice_items_invoice_id_line_no` |
| 4 | `INSERT INTO suppliers … ON CONFLICT (shop_id, normalized_name) WHERE NOT is_archived DO NOTHING`, then select the id (`SupplierRepository.FindOrCreateAsync`) | `ux_suppliers_shop_id_normalized_name` |
| 5 | `SELECT 1 FROM products WHERE id = ANY(@ids) AND shop_id = @shop ORDER BY id FOR UPDATE`, then load them with units (`StockService.LockProductsAsync`) | primary key |
| 6–7, 9–10 | `INSERT` new products and base units, `INSERT` one `InvoiceReceipt` movement per line, `UPDATE` products, invoice and every line, in one `SaveChanges` | `ux_stock_movements_invoice_item_id_reason` (last safety net) |
| 8 | one `INSERT INTO product_aliases … SELECT … FROM unnest(…) ON CONFLICT (shop_id, supplier_id, normalized_alias) DO UPDATE` (`ProductAliasRepository.UpsertAsync`) | `ux_product_aliases_shop_id_supplier_id_normalized_alias` (`NULLS NOT DISTINCT`) |

Step 8 runs after the save of step 10 (same transaction) because the aliases reference the new products' rows.

## 5. Integrations
| | |
|---|---|
| Why | Redis: catalog version, invoice status cache, supplier list (docs/design/redis.md) |
| Endpoint / operation | after commit: `INCR stockly:{shopId}:ver:catalog`; `SET stockly:{shopId}:invoice:{id}:status` (Confirmed); `DEL stockly:{shopId}:suppliers:list` when a supplier was created |
| Auth | Redis password from `.env` |
| Timeout / retry | 250 ms operation timeout, no retry |
| Failure behaviour | logged warning; the confirm still succeeds (rule 25) |
| Cost | none (no AI call) |

## 6. Cache keys
| Key | TTL | Invalidated by |
|---|---|---|
| `stockly:{shopId}:ver:catalog` | none | bumped after a confirmation this request made (not on a repeat, not on a failure) |
| `stockly:{shopId}:invoice:{id}:status` | 10 min | overwritten with `Confirmed` |
| `stockly:{shopId}:suppliers:list` | 30 min | deleted when confirm created a supplier |

## 7. Business rules
- Lock order: the invoice, then the existing products in one ordered statement (uuid order); no path locks a product before an invoice or a sale (rule 4).
- `Confirmed` → `200` with the invoice and nothing written (idempotent). `Processing`/`Failed` → `409 Invoice.NotConfirmable`. A line with neither product nor new product → `400 Invoice.LineProductRequired`. Missing or another shop's invoice → `404 Invoice.NotFound`.
- A concurrent second confirm waits on the invoice lock, then sees `Confirmed` and returns `200` without movements. If the unique index `(invoice_item_id, reason)` ever refuses a receipt, the attempt rolls back and the confirmed invoice is returned with `200`.
- Existing line: base quantity = `ProductUnit.ToBase(quantity)` (rule 5). New-product line: the quantity is already in the new base unit. One `InvoiceReceipt` per line; two lines of one product chain their `balance_after`. Quantities come from the locked rows, never from cache (rule 24). Archived products still receive stock (backend.md §17.1 item 11).
- New products (§10.5): lines with the same normalized name and normalized base unit name share one product; the lowest line number sets cost (= its unit price) and sell price; low-stock threshold 0; no opening `StockCount` movement (the receipt is the first movement). The line keeps `product_id = NULL` and gets `created_product_id`.
- Supplier: kept when the invoice already has one; else found or created from the name read on the invoice; else none.
- Aliases: one per normalized raw name; the highest line number decides product and unit; `times_confirmed` grows by the number of lines; `last_used_at` = confirmation time. A null supplier is a supplier too (`NULLS NOT DISTINCT`).
- `cost_price` of existing products is not changed (backend.md §17.1 item 12).

## 8. Error cases
| Case | Error code | HTTP | User message (ar / en) |
|---|---|---|---|
| Invoice missing or another shop's | `Invoice.NotFound` | 404 | لم يتم العثور على الفاتورة. / The invoice was not found. |
| Invoice processing or failed | `Invoice.NotConfirmable` | 409 | يمكن تأكيد الفاتورة فقط وهي بانتظار المراجعة. / Only an invoice waiting for review can be confirmed. |
| A line has no product | `Invoice.LineProductRequired` | 400 | اختر منتجاً موجوداً أو أدخل بيانات منتج جديد لكل بند. / Choose an existing product or describe a new product for every line. |

Internal only: `Invoice.AlreadyConfirmed` (unique-index safety net, answered with `200`), `Invoice.CreatedProductsMismatch` (Domain guard).

## 9. Configuration / env vars
None.

## 10. Mobile / Admin screens
Review screen "مراجعة الفاتورة": confirm button with loading state; on error show the localized `detail`; on `200` show the confirmed invoice (read-only). Admin: none.

## 11. Tests
| Project / class | Covers |
|---|---|
| `Domain.Tests` `Invoices/InvoiceConfirmTests` | Status, supplier, user, every line's `confirmed_at` and `created_product_id`; not confirmable; unresolved line; created products must match the new-product lines; user required |
| `Application.Tests` `Features/Invoices/ConfirmInvoiceCommandHandlerTests` | Happy path (base quantities, new product grouping and prices, aliases with counts, bump/status/supplier cache **after commit**); no supplier name; repeat (no writes, no bump); not found / other shop; processing; unresolved line; unique-index safety net (rollback, `200`, no cache work); other save failure; unauthenticated |
| `Infrastructure.Tests` `Persistence/Sprint4PersistenceTests` | Ordered multi-product lock and receipt; another shop's product not locked; movement refused for an unlocked product; supplier find-or-create once; alias upsert insert / repoint / count with a null supplier |
| `Api.IntegrationTests` `Invoices/ConfirmEndpointTests` | End to end on PostgreSQL + Redis: base quantities, chained balances, new product created once with its base unit, supplier once, aliases and `times_confirmed`, every line confirmed; second invoice reuses supplier and counts the alias; repeat `200` no new movements; **two parallel confirms → exactly one receipt per line, both `200`**; `409`, `400` with nothing changed, wrong tenant `404`; product list cache invalidated; Redis down still confirms |
| `Api.IntegrationTests` `Sales/SalesEndpointTests` | Confirm and sale on overlapping products in parallel: no deadlock |

## 12. Known limitations
- No reversal (`InvoiceReversal`) endpoint yet.
- `stockly:{shopId}:suppliers:list` has no reader yet (no supplier list endpoint); the delete keeps the key correct once one exists.
- A unit conversion that rounds a tiny quantity to `0.000` base units would be refused by the ledger (`Stock.ZeroChange`); not reachable with realistic factors.
