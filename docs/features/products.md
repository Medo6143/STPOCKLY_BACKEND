# Feature: Products

Status: Done (backend: domain in step 1.3, API in step 1.6) · Sprint: 1 · Owner: medo6143

## 1. Purpose
A shop owner keeps a catalog of the products they buy and sell, each with the units it is bought and sold in (strip, box, carton; kg, sack). Every stock change is recorded as a movement in an append-only ledger, so the quantity on screen can always be explained by the invoices, sales and corrections behind it. Step 1.6 exposes the catalog through the API: create (optionally with opening stock), edit, archive, list with search and a low-stock filter, units, manual adjustments, stock counts and the movement history.

## 2. User flow
1. **المنتجات** (Products): the list, sorted by name, with a search box (Arabic spelling variants match: `أ/إ/ا`, `ة/ه`, `ى/ي`, tashkeel ignored) and a **نواقص** (Low stock) filter. Archived products are hidden unless asked for (P1).
2. **منتج جديد** (New product): name, optional barcode and category, base unit name (the smallest unit sold), cost price, base unit sell price, optional low-stock threshold. The **Owner** may also enter the **رصيد أول المدة** (opening stock); it is recorded as a stock count noted "Opening stock" (P3).
3. **تفاصيل المنتج** (Product details): quantity, prices, units (base first) (P2); **تعديل** (Edit) changes name, barcode, category, cost price and threshold (P4).
4. **الوحدات** (Units): add a unit with its factor (how many base units it holds), sell price and barcode; edit or delete it. The base unit cannot be deleted and its factor stays 1 (P6–P8).
5. **تسوية المخزون** (Stock adjustment, Owner): a signed quantity in any unit plus a required note ("Expired, thrown away") (P9).
6. **جرد** (Stock count, Owner): the counted quantity in any unit; the difference becomes a movement, or nothing when it matches (P10).
7. **حركة المخزون** (Stock history): every movement, newest first (P11).
8. **أرشفة** (Archive, Owner): hides the product from the list; history stays and late corrections are still allowed (P5).

## 3. Endpoints
Contract v1.4 rows P1–P11 (`docs/api/contract.md` §5); machine-readable in `docs/api/openapi.json`. Money, quantities and factors are **decimal strings** in requests and responses (contract §1.2): responses use exactly 2 (money), 3 (quantity) and 6 (factor) decimals; a JSON number in their place is `400 Validation.Failed`.

| # | Method | Path | Auth / role | Request | Response | Status codes |
|---|---|---|---|---|---|---|
| P1 | GET | `/api/products` | user | query `search` (max 100), `lowStock`, `includeArchived` (both default `false`), `page` (default 1), `pageSize` (default 20, max 100) | `200 PagedResponse<ProductListItem>` | 200, 400, 401, 403 |
| P2 | GET | `/api/products/{id}` | user | none | `200 ProductDetail` | 200, 401, 403, 404 |
| P3 | POST | `/api/products` | user (`openingQuantity`: Owner) | `CreateProductRequest` | `201 ProductDetail`, `Location: /api/products/{id}` | 201, 400, 401, 403, 409 |
| P4 | PUT | `/api/products/{id}` | user | `UpdateProductRequest` | `200 ProductDetail` | 200, 400, 401, 403, 404, 409 |
| P5 | POST | `/api/products/{id}/archive` | owner | none | `204` (idempotent) | 204, 401, 403, 404 |
| P6 | POST | `/api/products/{id}/units` | user | `ProductUnitRequest` | `201 UnitDto`, `Location: /api/products/{id}/units/{unitId}` | 201, 400, 401, 403, 404, 409 |
| P7 | PUT | `/api/products/{id}/units/{unitId}` | user | `ProductUnitRequest` | `200 UnitDto` | 200, 400, 401, 403, 404, 409 |
| P8 | DELETE | `/api/products/{id}/units/{unitId}` | user | none | `204` | 204, 400, 401, 403, 404, 409 |
| P9 | POST | `/api/products/{id}/adjustments` | owner | `StockAdjustmentRequest` | `201 StockMovementDto` (`ManualAdjustment`) | 201, 400, 401, 403, 404 |
| P10 | POST | `/api/products/{id}/stock-counts` | owner | `StockCountRequest` | `200 StockCountResponse` | 200, 400, 401, 403, 404 |
| P11 | GET | `/api/products/{id}/movements` | user | query `page`, `pageSize` | `200 PagedResponse<StockMovementDto>`, newest first | 200, 400, 401, 403, 404 |

Code: `Api/Controllers/ProductsController` → MediatR → `Application/Features/Products/{GetProducts,GetLowStock,GetProductById,CreateProduct,UpdateProduct,ArchiveProduct,AddProductUnit,UpdateProductUnit,RemoveProductUnit,AdjustStock,CountStock,GetProductMovements}`. `lowStock=true` is served by `GetLowStockQuery`. DTOs: `Contracts/Products`, `Contracts/Common/PagedResponse<T>`; `MovementReason` is a contract enum serialized as its PascalCase name. Owner-only rows use the `Owner` policy (Staff → `403 Auth.Forbidden`); `openingQuantity` is checked in the handler (`403 Product.OpeningStockOwnerOnly`). Routes take `{id:guid}`: a malformed id is `404 Http.NotFound`.

Ports (Application) and adapters (Infrastructure):

| Port | Adapter | Role |
|---|---|---|
| `IProductRepository` | `Persistence/Repositories/ProductRepository` | Load a product with its units for an edit (tracked), add, barcode checks |
| `IProductQueries` | `Persistence/Queries/ProductQueries` | Read projections to contract DTOs, `AsNoTracking`, no N+1 |
| `IStockService` | `Persistence/Stock/StockService` | The only writer of `products.quantity` (row lock + Domain + new movement) |
| `ICacheService` | `Caching/RedisCacheService` | Read cache and catalog version counter |

## 4. Data model changes
Tables `products`, `product_units`, `stock_movements` (and `shops`) were created by migration `InitialCatalog` (step 1.4a). Step 1.6 adds migration **`ProductsCatalog`** (`20261012203200_ProductsCatalog`):

```sql
ALTER TABLE products ADD barcode text;
ALTER TABLE products ADD category text;
CREATE UNIQUE INDEX ux_products_shop_id_barcode ON products (shop_id, barcode) WHERE barcode IS NOT NULL AND NOT is_archived;
```

| Index | Query it serves |
|---|---|
| `ux_products_shop_id_barcode` (new) | P3/P4 duplicate-barcode check (`IsBarcodeUsedAsync`); barcode scan in the sale screen (Sprint 4) |
| `ix_products_shop_id_normalized_name` `(shop_id, normalized_name) WHERE NOT is_archived` | P1 default list, sorted by normalized name, paged |
| `ix_products_normalized_name_trgm` GIN `gin_trgm_ops` | P1 `search`: `normalized_name ILIKE '%term%'` |
| `ix_products_low_stock` `(shop_id) WHERE quantity <= low_stock_threshold AND NOT is_archived` | P1 `lowStock=true` (`GetLowStockQuery`) |
| `ux_product_units_shop_id_barcode` | P6/P7 unit barcode unique in the shop (`IsUnitBarcodeUsedAsync`) |
| `ix_stock_movements_shop_id_product_id_created_at` | P11 history, newest first |
| primary keys | P2, P4–P10 load by id; `StockService` locks the product row by id |

Full list: `docs/erd.md` §3.2 (queries served) and §3.7 (migration status). EF configuration: `Infrastructure/Persistence/Configurations`, one class per entity.

| Type | Kind | Notes |
|---|---|---|
| `Product` | Aggregate root, `ITenantEntity` | `Name`, `NormalizedName`, `Barcode` (step 1.6), `Category` (step 1.6), `Quantity` (private setter), `CostPrice` (per base unit), `LowStockThreshold` (base units, default 0), `IsArchived`, `CreatedAt`, `Units` |
| `ProductUnit` | Entity inside `Product`, `ITenantEntity` | `Name`, `Factor` (`numeric(14,6)`), `SellPrice`, `IsBase`, `Barcode` |
| `StockMovement` | Entity, `ITenantEntity`, append-only | `QuantityChange`, `BalanceAfter`, `Reason`, `InvoiceId`/`InvoiceItemId` or `SaleId`/`SaleItemId`, `Note`, `CreatedByUserId`, `CreatedAt` |
| `MovementReason` | Enum (smallint) | InvoiceReceipt 1, Sale 2, ManualAdjustment 3, StockCount 4, InvoiceReversal 5 |
| `MovementSource` | Value object | `None`, `FromInvoiceItem(invoiceId, itemId)`, `FromSaleItem(saleId, itemId)`; two sources cannot be built |

Notes:
- Ids are UUIDv7 from `Guid.CreateVersion7()`, assigned at construction (ADR-0010).
- `products.updated_at` is an EF shadow property set by the `AuditTimestampsInterceptor` from `IClock`; `ProductDetail.updatedAt` reads it. The Domain does not read the clock: methods that record time take `now`.
- **The base unit should be the smallest unit sold**, so most factors are >= 1 (Strip 1, Box 3, Carton 30). Fractional factors are allowed (Kg 1, Half kg 0.5).
- `stock_movements UNIQUE (invoice_item_id, reason) WHERE invoice_item_id IS NOT NULL`: see the reversal rule below and ADR-0001.

## 5. Integrations
| | PostgreSQL |
|---|---|
| Why | Catalog, units and the stock ledger |
| Endpoint / operation | `StocklyDbContext`; one transaction per command (`TransactionBehavior` → `IUnitOfWork`); stock changes lock the product row with `SELECT 1 FROM products WHERE id = @id AND shop_id = @shop FOR UPDATE` |
| Auth | Connection string from `.env` / environment |
| Timeout / retry | Npgsql defaults; no retry strategy |
| Failure behaviour | Database down → `500 Server.Unexpected` |
| Cost | P1: one `COUNT` and one page query; P2: one query with units; writes: a few indexed statements |

| | Redis (read cache, ADR-0007, `docs/design/redis.md`) |
|---|---|
| Why | P1 pages are read far more often than the catalog changes |
| Endpoint / operation | `IDistributedCache` (`GET`/`SET` with TTL) for pages; `IConnectionMultiplexer` `GET`/`INCR` for `ver:catalog`; one lazily opened connection |
| Auth | `ConnectionStrings__Redis` (password in `.env`) |
| Timeout / retry | `AbortOnConnectFail = false`, 250 ms operation timeout, 1 s first connect, `BacklogPolicy.FailFast` (no queueing while disconnected); no retry |
| Failure behaviour | Reads go to PostgreSQL, writes still succeed, one warning per minute (operation and exception type only). An unreadable version skips the cache entirely (never serves a possibly stale page). Never fails a request (AGENTS.md rule 25). |
| Cost | Free (self-hosted) |

No Hangfire, MinIO or OpenRouter in this feature.

## 6. Cache keys
| Key | TTL | Written by | Invalidated by |
|---|---|---|---|
| `stockly:{shopId}:ver:catalog` | none (tiny counter) | — | `INCR` after every successful product, unit or stock write (P3–P10), after commit |
| `stockly:{shopId}:products:list:v{ver}:{queryHash}` | 10 min | `GetProductsQueryHandler` (P1) | version bump |
| `stockly:{shopId}:products:lowstock:v{ver}:{queryHash}` | 5 min | `GetLowStockQueryHandler` (P1, `lowStock=true`) | version bump |

- `queryHash` = first 32 hex characters of SHA-256 over the normalized search, `lowStock`, `includeArchived`, `page` and `pageSize` (`CatalogListCache.QueryHash`).
- Key formats and TTLs live in one place: `Application/Abstractions/Caching/CacheKeys`. Values are the contract DTOs serialized with System.Text.Json.
- The bump runs in `CatalogCacheInvalidationBehavior`, which wraps `TransactionBehavior` for commands marked `IInvalidatesCatalog`: it runs after the commit and only on success (a rolled-back write bumps nothing). No `SCAN`, no pattern delete.
- Only the P1 lists are cached. P2, P11 and every value used by a write are read from PostgreSQL (rule 24).
- `docs/design/redis.md` already lists these keys; unchanged by this step.

## 7. Business rules

**Precision (all values)**
- Money (`CostPrice`, `SellPrice`): >= 0, at most 2 decimal places, < 10¹⁶.
- Quantities (`LowStockThreshold`, movement changes, counts, balances, opening stock): at most 3 decimal places, |value| < 10¹¹. Threshold, counted quantity and opening stock are >= 0.
- Unit factor: > 0, at most 6 decimal places, < 10⁸.
- Values with too many decimal places are **rejected** (`Precision.TooManyDecimals`), never rounded. The check is `decimal.Round(v, n) == v`, so `1.50` is valid money.

**Decimal strings in requests (contract §1.2)**
- Shape `^-?\d{1,17}(\.\d+)?$` with ASCII digits only; anything else (`"12,5"`, `"1e3"`, `" 1"`, `"+1"`, Arabic-Indic digits, a JSON number) is a request-shape error: `400 Validation.Failed` with the field in `errors`.
- One shared parser (`Application/Validation/DecimalInput`) then applies the Domain rules in this order: sign (`Precision.Negative`, or `Precision.NotPositive` for factors), decimal places counted on the text itself so digits beyond what `decimal` holds are still rejected (`Precision.TooManyDecimals`), range (`Precision.OutOfRange`). Trailing zeros are fine (`"7.000000"` is a valid money value 7). `"-0"` is zero.
- Per endpoint (contract): P3/P4 money and threshold → TooManyDecimals, Negative, OutOfRange; P6/P7 factor → NotPositive, plus the money codes for the price; P9 → TooManyDecimals, OutOfRange (signed); P10 → Negative, TooManyDecimals, OutOfRange.

**Product**
- `Product.Create(shopId, name, baseUnitName, costPrice, baseUnitSellPrice, lowStockThreshold, now, barcode, category)` creates the product with quantity 0 and its base unit (factor 1). The shop is the token's shop (ADR-0002), never a request value.
- Name must be non-empty after normalization (a name of only tashkeel is rejected). The name is stored trimmed; `NormalizedName` uses `ArabicTextNormalizer`.
- Barcode (max 64) and category (max 100) are optional, trimmed, blank → null. A barcode used by another **non-archived** product of the shop → `409 Product.DuplicateBarcode` (archiving frees it; other shops do not matter).
- `Update(name, barcode, category, costPrice, lowStockThreshold)` (P4) checks every value first and changes nothing on failure. Absent barcode/category clear them. The base unit's name and price change through P7.
- `Rename`, `ChangeCostPrice`, `SetLowStockThreshold`, `Update`, `AddUnit`, `UpdateUnit`, `RemoveUnit` are rejected on an archived product (`409 Product.Archived`).
- `Archive()` is idempotent (P5 always `204`). Stock movements stay allowed on archived products (a late sale, a final count, a late correction).
- `IsLowStock` is `Quantity <= LowStockThreshold`. With the default threshold 0, out-of-stock and negative-stock products are low stock.
- Quantity may be negative (a sale recorded before its invoice); it never blocks the cashier.

**Opening stock (P3, contract v1.1 decision 9)**
- `openingQuantity` is Owner only: a Staff request that includes it (any value) → `403 Product.OpeningStockOwnerOnly`, nothing is created. Staff may create products without it.
- Greater than zero: `IStockService.RecordOpeningStock` → `Product.RecordOpeningStock` records a `StockCount` movement noted `Product.OpeningStockNote` ("Opening stock") in the **same transaction** as the product. Absent or `"0.000"`: quantity `"0.000"`, no movement.

**Units**
- Unit names are unique per product, compared after normalization (`شريط` = `شَريـط`) → `409 ProductUnit.DuplicateName`.
- Unit barcodes are unique in the shop, across all products (archived included) → `409 ProductUnit.DuplicateBarcode`; checked in the handler and enforced by `ux_product_units_shop_id_barcode`.
- **Concurrent duplicate barcodes** (P3, P4, P6, P7): when another request commits the same barcode between the handler's check and its save, the unique index rejects the insert or update. `IUnitOfWork.TrySaveChangesAsync` (Infrastructure `UnitOfWork`) turns a PostgreSQL unique violation (SqlState `23505`) on `ux_products_shop_id_barcode` into `409 Product.DuplicateBarcode` and on `ux_product_units_shop_id_barcode` into `409 ProductUnit.DuplicateBarcode`; the handler returns it, so the transaction rolls back (no partial product, no opening-stock movement). Any other unique violation stays `500 Server.Unexpected`. Application never sees Npgsql.
- The base unit cannot be removed (`400 ProductUnit.BaseUnitCannotBeRemoved`) and its factor is always 1 (`400 ProductUnit.BaseUnitFactorFixed`). Its name, sell price and barcode can change.
- `ToBase(quantity) = round(quantity × factor, 3, MidpointRounding.AwayFromZero)`.
- Units in `ProductDetail`: base unit first, then factor ascending, then name.
- `ProductUnit.InUse` (P8 `409`) needs invoice and sale lines, which arrive in Sprint 2+; until then P8's `409` is `Product.Archived` only.

**Stock ledger**
- `Product.RecordMovement(quantityChange, reason, source, userId, note, now)` is the **only** way to change `Quantity`. It validates first and, on success, sets `Quantity` to the movement's `BalanceAfter`. On any failure nothing changes. Only `IStockService` calls it (through `AdjustStock`, `CountStock`, `RecordOpeningStock`), inside the database transaction (AGENTS.md rule 1).
- `IStockService` locks the product row first (`FOR UPDATE`, restricted to the token's shop), then reads it with its units (a new statement under READ COMMITTED sees the latest committed balance), calls the Domain and adds the movement to the same unit of work. It refuses to run outside a transaction. One product per request, so the lock order rule (products by id) holds trivially (rule 4). Concurrent adjustments on one product queue up and produce a gap-free `balance_after` chain.
- A movement never has a zero change and always has a non-empty user (the token's `sub`).

| Reason | Source | Change | Other |
|---|---|---|---|
| InvoiceReceipt | invoice + invoice line | > 0 | |
| Sale | sale + sale line | < 0 | |
| ManualAdjustment | none | ≠ 0 | note required (P9, max 500) |
| StockCount | none | ≠ 0 | optional note ("Opening stock") |
| InvoiceReversal | invoice + invoice line | < 0 | terminal |

- **P9** `AdjustStock(unitId, quantityChange, userId, note, now)`: the change is in the given unit (`null` = base unit), validated (3 decimals) before conversion, then converted with `ToBase`. Zero → `400 Stock.ZeroChange`; missing/blank note → `400 Stock.NoteRequired`; unknown unit → `404 ProductUnit.NotFound`.
- **P10** `CountStock(unitId, countedQuantity, userId, now)`: counted >= 0 in the given unit, converted with `ToBase`; change = counted − current. Equal → `changed: false`, `movement: null`, **no movement**. Validation errors are returned even when nothing would change.
- Source references must not be empty Guids. Notes are trimmed; a blank note is stored as null.
- **A reversal is terminal.** A reversed invoice line can never be received again; corrections go through a new scan or a manual adjustment. This makes `UNIQUE (invoice_item_id, reason)` sufficient. Matching the reversal amount to the original receipt is an Application rule (the aggregate cannot see the receipt). No reversal endpoint is scheduled yet.

**List and search (P1)**
- `search` is normalized with `ArabicTextNormalizer` and matched with `normalized_name ILIKE '%term%'` (wildcards `%`, `_` and `\` in the term are matched literally). A search that is blank after normalization means no search.
- Sorted by `normalized_name`, then id (stable paging). `lowStock=true` keeps `quantity <= low_stock_threshold`; `includeArchived=false` (default) hides archived products. `pageSize > 100`, `pageSize < 1` or `page < 1` → `400 Validation.Failed` (never clamped).
- `totalCount` counts every matching product.

**Arabic text normalization** (`ArabicTextNormalizer.Normalize`)
- Removes tashkeel (U+064B–U+0652, U+0670) and tatweel (U+0640).
- أ إ آ ٱ → ا · ة → ه · ى → ي.
- Arabic-Indic ٠-٩ and Persian ۰-۹ → 0-9.
- Latin lower-case (invariant), trim, collapse any whitespace to one space. Mixed scripts stay mixed: `بنادول Extra 500` → `بنادول extra 500`.

**Tenancy and errors**
- Every query runs under the tenant filter; another shop's product (or unit) is `404 Product.NotFound` (`ProductUnit.NotFound`), never `403` (AGENTS.md rule 10).
- Order of checks in a write: request shape (`400 Validation.Failed`) → decimal precision (`400 Precision.*`) → product found (`404`) → Domain rules (`409 Product.Archived`, `400`/`409` unit and stock rules) → database uniqueness checks (`409` duplicates).

## 8. Error cases
| Case | Error code | HTTP | User message (ar / en) |
|---|---|---|---|
| Request shape invalid (missing field, malformed decimal string, JSON number for a decimal, `pageSize > 100`, long barcode/category/search/note) | `Validation.Failed` (+ `errors`) | 400 | حقل واحد أو أكثر غير صالح. راجع 'errors'. / One or more fields are invalid. See 'errors'. Malformed decimal field: يجب أن تكون القيمة رقماً عشرياً مكتوباً كنص، مثل "12.50". / Must be a decimal number written as a string, e.g. "12.50". |
| Product not in this shop (also another shop's) | `Product.NotFound` | 404 | المنتج غير موجود في هذا المتجر. / The product was not found in this shop. |
| Product name blank after normalization | `Product.NameRequired` | 400 | اسم المنتج مطلوب. / The product name is required. |
| Edit an archived product (P4, P6–P8) | `Product.Archived` | 409 | هذا المنتج مؤرشف ولا يمكن تعديله. / This product is archived and cannot be edited. |
| Barcode used by another active product | `Product.DuplicateBarcode` | 409 | يوجد منتج نشط آخر في هذا المتجر يستخدم هذا الباركود. / Another active product in this shop already uses this barcode. |
| Staff sends `openingQuantity` | `Product.OpeningStockOwnerOnly` | 403 | صاحب المتجر فقط يمكنه تحديد رصيد أول المدة. / Only the shop owner can set the opening stock. |
| Staff calls P5, P9 or P10 | `Auth.Forbidden` | 403 | دورك في المتجر لا يسمح بهذا الإجراء. / Your role does not allow this action. |
| Unit not on this product | `ProductUnit.NotFound` | 404 | الوحدة غير موجودة في هذا المنتج. / The unit was not found on this product. |
| Unit name blank | `ProductUnit.NameRequired` | 400 | اسم الوحدة مطلوب. / The unit name is required. |
| Unit name already on the product | `ProductUnit.DuplicateName` | 409 | هذا المنتج لديه وحدة بنفس الاسم بالفعل. / This product already has a unit with this name. |
| Unit barcode already used in the shop | `ProductUnit.DuplicateBarcode` | 409 | توجد وحدة في هذا المتجر تستخدم هذا الباركود بالفعل. / A unit in this shop already uses this barcode. |
| Delete the base unit | `ProductUnit.BaseUnitCannotBeRemoved` | 400 | لا يمكن حذف الوحدة الأساسية. / The base unit cannot be removed. |
| Change the base unit factor | `ProductUnit.BaseUnitFactorFixed` | 400 | معامل الوحدة الأساسية دائماً 1. / The base unit factor is always 1. |
| Too many decimal places | `Precision.TooManyDecimals` | 400 | القيمة بها أرقام عشرية أكثر من المسموح (المبالغ 2، الكميات 3، المعامل 6). / The value has too many decimal places (money 2, quantity 3, factor 6). |
| Negative money, threshold, count or opening stock | `Precision.Negative` | 400 | يجب ألا تكون القيمة سالبة. / The value must not be negative. |
| Factor zero or negative | `Precision.NotPositive` | 400 | يجب أن تكون القيمة أكبر من صفر. / The value must be greater than zero. |
| Value does not fit the column | `Precision.OutOfRange` | 400 | القيمة كبيرة جداً. / The value is too large. |
| Adjustment of zero | `Stock.ZeroChange` | 400 | يجب ألا يكون التغيير في الكمية صفراً. / The quantity change must not be zero. |
| Adjustment without a note | `Stock.NoteRequired` | 400 | يجب كتابة ملاحظة توضح سبب التعديل. / A note explaining the adjustment is required. |
| Missing/invalid token · suspended shop · malformed id in the path | `Auth.Unauthenticated` · `Shop.Suspended` · `Http.NotFound` | 401 · 403 · 404 | see `docs/features/auth.md` |

Domain-only codes that requests cannot reach (validators or the pipeline reject first): `Product.ShopRequired`, `Product.BarcodeTooLong`, `Product.CategoryTooLong`, `Stock.UserRequired`, and the invoice/sale movement codes (`Stock.UnknownReason`, `Stock.InvoiceSourceRequired`, `Stock.SaleSourceRequired`, `Stock.SourceNotAllowed`, `Stock.EmptySourceReference`, `Stock.ChangeMustBePositive`, `Stock.ChangeMustBeNegative`) used from Sprint 2.

## 9. Configuration / env vars
| Name | Example | Secret? |
|---|---|---|
| `ConnectionStrings__Redis` | `127.0.0.1:6379,password=change-me-redis,abortConnect=false` (already required since step 1.4) | **Yes** (password) |

No new settings. Timeouts (250 ms operation, 1 s first connect) are code constants (`Infrastructure/Caching/RedisConnection`).

## 10. Mobile / Admin screens
Sprint 3 (stockly-mobile): product list with search and low-stock filter, product details, create/edit, units, adjustment, stock count, history; every screen with the 4 states (loading, empty, error, data), RTL first, decimals parsed with the `decimal` package (never `double`). Admin: none.

## 11. Tests
| Project / class | Covers |
|---|---|
| `Domain.Tests` `Common/PrecisionTests`, `Text/ArabicTextNormalizerTests` | Each limit for money, quantity, factor; rejection without rounding; normalization cases |
| `Domain.Tests` `Products/ProductCreateTests`, `ProductBehaviourTests`, `ProductUnitTests` | Create, rename, prices, threshold, archive, `IsLowStock`; units, base unit protection, duplicates, `ToBase` rounding |
| `Domain.Tests` `Products/ProductDetailsTests` (1.6) | Barcode/category trimmed, blank → null, max lengths; `Update` all-or-nothing, every rejection, archived first; `FindUnit` |
| `Domain.Tests` `Stock/StockMovementRulesTests`, `StockLedgerSequenceTests` | Each reason's happy path and every rejection; balance chain |
| `Domain.Tests` `Stock/StockInUnitsTests` (1.6) | Adjust and count in other units (conversion, rounding), unknown unit, zero change, note required, precision before conversion, archived allowed; count with note; opening stock (positive, zero, negative) |
| `Application.Tests` `Validation/DecimalInputTests` | Shape, every precision code per kind, digits beyond `decimal`, `-0`, field errors, first-error order |
| `Application.Tests` `Features/Products/ProductMappingTests` | Fixed decimals, invariant culture, contract ↔ Domain enum alignment, unit and movement DTOs (UTC time) |
| `Application.Tests` `Features/Products/ProductValidatorTests` | Request shape of P1–P11 (required fields, decimal shape, lengths, paging bounds) |
| `Application.Tests` `Features/Products/CreateProductCommandHandlerTests`, `UpdateAndArchiveProductHandlerTests`, `ProductUnitHandlerTests`, `StockHandlerTests`, `ProductQueryHandlerTests` | Every handler: happy path, validation/precision failure, not found, wrong tenant (another shop's product), Domain conflicts, duplicate barcodes, opening stock Owner/Staff, cache hit/miss/Redis-down, key formats and TTLs, query hash |
| `Application.Tests` `Behaviors/CatalogCacheInvalidationBehaviorTests` | Bump after the inner pipeline (commit), not on failure, not without a tenant; registered outside `TransactionBehavior` |
| `Infrastructure.Tests` `Persistence/StockServiceTests` (Testcontainers PostgreSQL) | Adjust in a unit saves base quantity and movement together; count changed/unchanged; other shop → `Product.NotFound`; unknown unit; opening stock in the product's transaction; refuses to run outside a transaction; **a second transaction waits for the first one's row lock and chains on its balance**; **10 concurrent adjustments in separate scopes end at the right quantity with a gap-free `balance_after` chain** |
| `Infrastructure.Tests` `Persistence/ProductQueriesTests` | Sort and paging, formatted list item with base unit, Arabic-normalized search, literal wildcards, low-stock and archived filters, tenant isolation, detail unit order and timestamps, movements newest first and paged, repository barcode checks (archived, self, other shops), tracked load with units, the new unique index |
| `Infrastructure.Tests` `Caching/RedisCacheServiceTests` (Testcontainers Redis) | Round trip with TTL, missing key, version counter (no TTL); **Redis down**: every operation degrades without throwing, version unknown, one throttled warning a minute without the connection string, fail fast |
| `Infrastructure.Tests` `Persistence/UnitOfWorkConflictTests` | Two transactions race on the same product barcode and the same unit barcode: the second blocks on the index and loses with `Product.DuplicateBarcode` / `ProductUnit.DuplicateBarcode`, nothing of it is saved; another unique violation still throws; no conflict succeeds |
| `Infrastructure.Tests` `Persistence/MigrationTests`, `GuardTests` | New index and its predicate, nullable text columns, no pending model changes; Redis test image equals docker-compose |
| `Api.IntegrationTests` `Products/ProductsEndpointTests`, `Products/ProductUnitsAndStockEndpointTests` | P1–P11 end to end: status codes and `code`s of every listed error, decimal strings in responses, JSON number rejected, `Location` headers, Owner/Staff, wrong tenant 404, suspended shop, list cache versioning (keys, TTL, bump after commit, none after a failed write) and Redis down |
| `Api.IntegrationTests` `Products/BarcodeRaceEndpointTests` | A competing transaction holds the barcode uncommitted; P3 and P6 pass their check, block on the unique index, and return `409 Product.DuplicateBarcode` / `409 ProductUnit.DuplicateBarcode` once it commits; the losing product and its opening stock roll back; two parallel requests (`Task.WhenAll`) with the same new product barcode, and with the same new unit barcode, give exactly one `201` and one `409` with the right code and one row with that barcode; another unique violation (`ux_product_units_product_id_name`) stays `500 Server.Unexpected` without details |
| `Api.IntegrationTests` `ContractConformanceTests`, `OpenApiDocumentTests`, `Errors/ErrorTextTests`, `Identity/HttpContextCurrentUserTests` | P1–P11 in the spec with exactly the contract statuses; committed spec current; every new code has ar and en text; `role` claim parsing |

## 12. Known limitations
- No lot tracking; expiry is informational (ADR-0004).
- `ProductUnit.InUse` is not implemented until invoice and sale lines exist (Sprint 2+).
- The low-stock partial index serves the default `lowStock=true` list; `lowStock=true&includeArchived=true` reads archived rows without it (rare admin-style query).
- Reversal amount matching is not in the Domain (Application rule, no endpoint scheduled yet).
- Product and unit names have no maximum length (the contract sets none).
