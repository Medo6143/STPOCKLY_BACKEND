# Feature: Products

Status: In progress (domain done in step 1.3; API in step 1.6) · Sprint: 1 · Owner: medo6143

## 1. Purpose
A shop owner keeps a catalog of the products they buy and sell, each with the units it is bought and sold in (strip, box, carton; kg, sack). Every stock change is recorded as a movement in an append-only ledger, so the quantity on screen can always be explained by the invoices, sales and corrections behind it.

## 2. User flow
Step 1.6.

## 3. Endpoints
Step 1.6.

## 4. Data model changes
Domain model only in this step; tables, the migration and EF configuration come in step 1.4. See `docs/erd.md` §3.2 and §3.5.

| Type | Kind | Notes |
|---|---|---|
| `Product` | Aggregate root, `ITenantEntity` | `Name`, `NormalizedName`, `Quantity` (private setter), `CostPrice` (per base unit), `LowStockThreshold` (base units, default 0), `IsArchived`, `CreatedAt`, `Units` |
| `ProductUnit` | Entity inside `Product`, `ITenantEntity` | `Name`, `Factor` (`numeric(14,6)`), `SellPrice`, `IsBase`, `Barcode` |
| `StockMovement` | Entity, `ITenantEntity`, append-only | `QuantityChange`, `BalanceAfter`, `Reason`, `InvoiceId`/`InvoiceItemId` or `SaleId`/`SaleItemId`, `Note`, `CreatedByUserId`, `CreatedAt` |
| `MovementReason` | Enum (smallint) | InvoiceReceipt 1, Sale 2, ManualAdjustment 3, StockCount 4, InvoiceReversal 5 |
| `MovementSource` | Value object | `None`, `FromInvoiceItem(invoiceId, itemId)`, `FromSaleItem(saleId, itemId)`; two sources cannot be built |

Notes:
- Ids are UUIDv7, assigned at construction (ADR-0009).
- `products.updated_at` is set by the `AuditTimestampsInterceptor` in step 1.4; the Domain does not read the clock. Methods that record time take `now`.
- **The base unit should be the smallest unit sold**, so most factors are >= 1 (Strip 1, Box 3, Carton 30). Fractional factors are allowed (Kg 1, Half kg 0.5).
- Index change from this step: `stock_movements UNIQUE (invoice_item_id, reason) WHERE invoice_item_id IS NOT NULL` (was `UNIQUE (invoice_item_id)`), see the reversal rule below and ADR-0001.
- Product `barcode` and `category` are added with the API in step 1.6.

## 5. Integrations
None in the domain. Step 1.6 (Redis catalog cache).

## 6. Cache keys
Step 1.6.

## 7. Business rules

**Precision (all values)**
- Money (`CostPrice`, `SellPrice`): >= 0, at most 2 decimal places, < 10¹⁶.
- Quantities (`LowStockThreshold`, movement changes, counts, balances): at most 3 decimal places, |value| < 10¹¹. Threshold and counted quantity are >= 0.
- Unit factor: > 0, at most 6 decimal places, < 10⁸.
- Values with too many decimal places are **rejected** (`Precision.TooManyDecimals`), never rounded. The check is `decimal.Round(v, n) == v`, so `1.50` is valid money.

**Product**
- `Product.Create(shopId, name, baseUnitName, costPrice, baseUnitSellPrice, lowStockThreshold, now)` creates the product with quantity 0 and its base unit (factor 1).
- Shop id must not be empty. Name must be non-empty after normalization (a name of only tashkeel is rejected). The name is stored trimmed; `NormalizedName` uses `ArabicTextNormalizer`.
- `Rename`, `ChangeCostPrice`, `SetLowStockThreshold`, `AddUnit`, `UpdateUnit`, `RemoveUnit` are rejected on an archived product (`Product.Archived`, Conflict).
- `Archive()` is idempotent. Stock movements stay allowed on archived products (a late sale, a final count).
- `IsLowStock` is `Quantity <= LowStockThreshold`. With the default threshold 0, out-of-stock and negative-stock products are low stock.
- Quantity may be negative (a sale recorded before its invoice); it never blocks the cashier.

**Units**
- Unit names are unique per product, compared after normalization (`شريط` = `شَريـط`).
- Unit barcodes are unique within the product. Uniqueness across the shop is a database index (step 1.4).
- The base unit cannot be removed and its factor is always 1. Its name, sell price and barcode can change.
- `ToBase(quantity) = round(quantity × factor, 3, MidpointRounding.AwayFromZero)`.

**Stock ledger**
- `Product.RecordMovement(quantityChange, reason, source, userId, note, now)` is the **only** way to change `Quantity`. It validates first and, on success, sets `Quantity` to the movement's `BalanceAfter`. On any failure nothing changes. `IStockService` calls it inside the database transaction (AGENTS.md rule 1).
- A movement never has a zero change and always has a non-empty user.

| Reason | Source | Change | Other |
|---|---|---|---|
| InvoiceReceipt | invoice + invoice line | > 0 | |
| Sale | sale + sale line | < 0 | |
| ManualAdjustment | none | ≠ 0 | note required |
| StockCount | none | ≠ 0 | |
| InvoiceReversal | invoice + invoice line | < 0 | terminal |

- Source references must not be empty Guids. Notes are trimmed; a blank note is stored as null.
- `RecordStockCount(countedQuantity, userId, now)`: counted >= 0; change = counted − current. If the change is 0 the outcome is `IsUnchanged` and **no movement** is created. Validation errors (precision, empty user) are returned even when nothing would change.
- **A reversal is terminal.** A reversed invoice line can never be received again; corrections go through a new scan or a manual adjustment. This makes `UNIQUE (invoice_item_id, reason)` sufficient. Matching the reversal amount to the original receipt is an Application rule (the aggregate cannot see the receipt). No reversal endpoint is scheduled yet.

**Arabic text normalization** (`ArabicTextNormalizer.Normalize`)
- Removes tashkeel (U+064B–U+0652, U+0670) and tatweel (U+0640).
- أ إ آ ٱ → ا · ة → ه · ى → ي.
- Arabic-Indic ٠-٩ and Persian ۰-۹ → 0-9.
- Latin lower-case (invariant), trim, collapse any whitespace to one space. Mixed scripts stay mixed: `بنادول Extra 500` → `بنادول extra 500`.

## 8. Error cases
Domain error codes (HTTP mapping and user messages in step 1.6):

| Case | Error code | Type |
|---|---|---|
| Empty shop id | `Product.ShopRequired` | Validation |
| Blank product name | `Product.NameRequired` | Validation |
| Edit an archived product | `Product.Archived` | Conflict |
| Blank unit name | `ProductUnit.NameRequired` | Validation |
| Duplicate unit name / barcode in product | `ProductUnit.DuplicateName` / `ProductUnit.DuplicateBarcode` | Conflict |
| Unknown unit id | `ProductUnit.NotFound` | NotFound |
| Remove base unit / change its factor | `ProductUnit.BaseUnitCannotBeRemoved` / `ProductUnit.BaseUnitFactorFixed` | Validation |
| Too many decimals / negative / not positive / too large | `Precision.TooManyDecimals` / `Precision.Negative` / `Precision.NotPositive` / `Precision.OutOfRange` | Validation |
| Movement rule broken | `Stock.ZeroChange`, `Stock.UserRequired`, `Stock.UnknownReason`, `Stock.InvoiceSourceRequired`, `Stock.SaleSourceRequired`, `Stock.SourceNotAllowed`, `Stock.EmptySourceReference`, `Stock.ChangeMustBePositive`, `Stock.ChangeMustBeNegative`, `Stock.NoteRequired` | Validation |

## 9. Configuration / env vars
None.

## 10. Mobile / Admin screens
Sprint 3.

## 11. Tests
`backend/tests/Stockly.Domain.Tests` (pure, no I/O):

| Class | Covers |
|---|---|
| `Common/EntityTests` | UUIDv7 ids, uniqueness, time order, equality by type and id |
| `Common/ResultTests` | Success/failure, `Value`/`Error` guards, implicit conversions, explicit `ErrorType` values |
| `Common/PrecisionTests` | Each limit for money, quantity, factor; rejection without rounding |
| `Text/ArabicTextNormalizerTests` | 24 theory cases (alef forms, ة, ى, tashkeel, U+0670, tatweel, both digit sets, Latin case, whitespace, mixed scripts, empty) + spelling variants, idempotency, null |
| `Products/ProductCreateTests` | Happy path with base unit; every rejection |
| `Products/ProductBehaviourTests` | Rename, cost price, threshold, archive rules, `IsLowStock` |
| `Products/ProductUnitTests` | Add/update/remove units, base unit protection, duplicates, factor rules, `ToBase` rounding |
| `Stock/StockMovementRulesTests` | Each reason's happy path and every rejection, zero change, user, unknown reason, precision, balance bound, notes |
| `Stock/StockLedgerSequenceTests` | Receipt → sale → count → negative sale → adjustment with `BalanceAfter` checked each time; failed movements after successful ones leave quantity and last balance unchanged; count without change; count rules |

Step 1.3 result: 178 tests, Domain line coverage 99.1%, branch 99.5%. Handler and API tests come in step 1.6.

## 12. Known limitations
- No lot tracking; expiry is informational (ADR-0004).
- Shop-wide barcode uniqueness is enforced only by the database (step 1.4).
- Reversal amount matching is not in the Domain (Application rule, no endpoint scheduled yet).
