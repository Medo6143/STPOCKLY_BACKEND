# Stockly API contract

**Version: v1.1** · Status: **Approved** (checkpoint 1, 2026-10-12) · Owner: orchestrator

Change log: v1 (2026-10-12) first draft · v1.1 (2026-10-12) opening stock on create (P3), list price and override flag on sales (S1, D1), `newProduct` without category (I4), `GET /api/auth/me` fields made explicit (A5).

This is the binding contract between `stockly-backend` and `stockly-mobile` (and `Stockly.Admin` later) for Sprints 1–4. Field names, types, status codes and error codes here are exact. Only the orchestrator changes this file: every change bumps the version, updates the copy in `stockly-mobile/docs/api/contract.md`, and is announced to the running lanes.

The machine-readable spec is `docs/api/openapi.json`. A backend test checks that every **implemented** row of the [endpoint index](#2-endpoint-index) matches `openapi.json` (path, method, status codes), and that `openapi.json` has no operation missing from the index.

---

## 1. Conventions

### 1.1 Transport
- Base path: `/api`. HTTPS in production. JSON bodies use `Content-Type: application/json; charset=utf-8`, except the scan upload (`multipart/form-data`).
- JSON property names are **camelCase**. Unknown request properties are ignored. `null` and absent are equivalent for optional fields.
- Enum values are **PascalCase strings** (`"InvoiceReceipt"`, `"Owner"`), never numbers.

### 1.2 Value formats

| Kind | JSON type | Format | Example |
|---|---|---|---|
| Id | string | UUID, lower-case, hyphenated | `"0192f0a0-6c1e-7b3a-9d2e-4f5a6b7c8d9e"` |
| Money (EGP) | **string** | exactly 2 decimals in responses; requests: up to 2 decimals, `>= 0` | `"125.50"` |
| Quantity (base or unit) | **string** | exactly 3 decimals in responses; requests: up to 3 decimals, sign allowed only where stated | `"1.500"`, `"-2.000"` |
| Unit factor | **string** | exactly 6 decimals in responses; requests: up to 6 decimals, `> 0` | `"3.000000"` |
| Match confidence | **string** | 2 decimals, `0.00`–`1.00` | `"0.85"` |
| Timestamp | string | ISO-8601 UTC with `Z`, up to microseconds | `"2026-10-12T07:00:00Z"` |
| Date | string | `yyyy-MM-dd` | `"2027-03-31"` |

- Money and quantities are **decimal strings** because Dart `double` cannot represent them exactly. The mobile app parses them with the `decimal` package. JSON numbers are rejected for these fields (`400 Validation.Failed`).
- Request decimal strings match `^-?\d{1,17}(\.\d+)?$`. More decimal places than allowed → `400 Precision.TooManyDecimals` (never rounded). Out of the column range → `400 Precision.OutOfRange`.

### 1.3 Paging
- Query: `page` (int, default `1`, `>= 1`), `pageSize` (int, default `20`, `1..100`). `pageSize > 100` → `400 Validation.Failed` (never silently clamped, AGENTS.md rule 30).
- Response:

```json
{ "items": [ ], "page": 1, "pageSize": 20, "totalCount": 137 }
```

### 1.4 Authentication
- `Authorization: Bearer <accessToken>` on every endpoint except register, login and refresh.
- **Access token**: JWT, lifetime **15 minutes**. Claims: `sub` (user id), `shop_id` (active shop), `role` (`Owner` | `Staff`). The shop comes only from the token, never from a request (ADR-0002).
- **Refresh token**: opaque random string, lifetime **30 days**, stored hashed. Every refresh **rotates** it (the old one is revoked). Presenting a revoked refresh token **revokes the whole chain** and returns `401 Auth.RefreshTokenReused` (AGENTS.md rule 13).
- **Client rule:** on `401` from any authenticated endpoint, the client calls `POST /api/auth/refresh` **once**, then retries the original request **once**. Concurrent `401`s share one refresh (request queue). If the refresh fails, the client signs out.
- Roles: `user` = Owner or Staff of the active shop. `owner` = Owner only. A Staff user calling an owner-only endpoint gets `403 Auth.Forbidden`.
- A suspended shop (`ShopStatus.Suspended`) gets `403 Shop.Suspended` on login, refresh and every authenticated call.

### 1.5 Localization
- `Accept-Language: ar` or `en` (default `ar`). It selects the language of ProblemDetails `title`/`detail` and validation messages. Codes never change with the language.
- Product and shop names are returned exactly as stored.

### 1.6 Idempotency
- `POST /api/invoices/scan` and `POST /api/sales` require a client-generated `clientRequestId` (UUID, created once per scan/sale and reused on every retry). A repeat with the same `clientRequestId` in the same shop returns the **existing** resource with `200` instead of creating a new one (ADR-0006).
- `POST /api/invoices/{id}/confirm` is idempotent: confirming a confirmed invoice returns `200` with the confirmed invoice and changes nothing.

### 1.7 Correlation and tracing
- Optional request header `X-Correlation-Id` (1–64 chars of `[A-Za-z0-9._-]`); otherwise the server creates one. It is always echoed in the response header and in ProblemDetails.

### 1.8 Errors (RFC 7807)
Every error response is `application/problem+json`:

```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.10",
  "title": "Conflict",
  "status": 409,
  "detail": "This product already has a unit with this name.",
  "instance": "POST /api/products/0192.../units",
  "code": "ProductUnit.DuplicateName",
  "traceId": "00-...-...-00",
  "correlationId": "abc-123"
}
```

- `code` is always present and stable; clients switch on `code`, never on `title`/`detail`.
- Request-shape validation (FluentValidation) returns `400` with `code: "Validation.Failed"` and `errors`: `{ "<camelCaseField>": ["<localized message>"] }`. Nested fields use dots and indexes: `"items[2].quantity"`.
- Mapping from `ErrorType`: Validation → 400, Unauthorized → 401, Forbidden → 403, NotFound → 404, Conflict → 409, Failure → 500.
- **Wrong tenant is 404**, with the resource's `NotFound` code (AGENTS.md rule 10).
- Unexpected server errors: `500`, `code: "Server.Unexpected"`, no exception details.
- Error codes that can occur on **every** authenticated endpoint are not repeated per endpoint: `401 Auth.Unauthenticated`, `403 Shop.Suspended`, `400 Validation.Failed`, `500 Server.Unexpected`.

---

## 2. Endpoint index

`State`: `implemented` = live in the backend and in `openapi.json`; `planned` = binding shape, not built yet. The conformance test checks implemented rows only. `Statuses` lists every status the operation can return (shared ones included).

| # | Method | Path | Auth | Statuses | Step | State |
|---|---|---|---|---|---|---|
| A1 | POST | /api/auth/register | anonymous | 201, 400, 409 | 1.5 | planned |
| A2 | POST | /api/auth/login | anonymous | 200, 400, 401, 403 | 1.5 | planned |
| A3 | POST | /api/auth/refresh | anonymous | 200, 400, 401, 403 | 1.5 | planned |
| A4 | POST | /api/auth/logout | user | 204, 400, 401 | 1.5 | planned |
| A5 | GET | /api/auth/me | user | 200, 401, 403 | 1.5 | planned |
| P1 | GET | /api/products | user | 200, 400, 401, 403 | 1.6 | planned |
| P2 | GET | /api/products/{id} | user | 200, 401, 403, 404 | 1.6 | planned |
| P3 | POST | /api/products | user | 201, 400, 401, 403, 409 | 1.6 | planned |
| P4 | PUT | /api/products/{id} | user | 200, 400, 401, 403, 404, 409 | 1.6 | planned |
| P5 | POST | /api/products/{id}/archive | owner | 204, 401, 403, 404 | 1.6 | planned |
| P6 | POST | /api/products/{id}/units | user | 201, 400, 401, 403, 404, 409 | 1.6 | planned |
| P7 | PUT | /api/products/{id}/units/{unitId} | user | 200, 400, 401, 403, 404, 409 | 1.6 | planned |
| P8 | DELETE | /api/products/{id}/units/{unitId} | user | 204, 400, 401, 403, 404, 409 | 1.6 | planned |
| P9 | POST | /api/products/{id}/adjustments | owner | 201, 400, 401, 403, 404 | 1.6 | planned |
| P10 | POST | /api/products/{id}/stock-counts | owner | 200, 400, 401, 403, 404 | 1.6 | planned |
| P11 | GET | /api/products/{id}/movements | user | 200, 400, 401, 403, 404 | 1.6 | planned |
| P12 | GET | /api/products/lookup | user | 200, 400, 401, 403, 404 | 4 | planned |
| I1 | POST | /api/invoices/scan | user | 200, 202, 400, 401, 403, 413, 429 | 2 | planned |
| I2 | GET | /api/invoices | user | 200, 400, 401, 403 | 2 | planned |
| I3 | GET | /api/invoices/{id} | user | 200, 401, 403, 404 | 2 | planned |
| I4 | PUT | /api/invoices/{id}/items | user | 200, 400, 401, 403, 404, 409 | 2 | planned |
| I5 | POST | /api/invoices/{id}/confirm | user | 200, 400, 401, 403, 404, 409 | 4 | planned |
| S1 | POST | /api/sales | user | 200, 201, 400, 401, 403, 404 | 4 | planned |
| S2 | GET | /api/sales | user | 200, 400, 401, 403 | 4 | planned |
| S3 | GET | /api/sales/{id} | user | 200, 401, 403, 404 | 4 | planned |
| D1 | GET | /api/dashboard/summary | user | 200, 401, 403 | 4 | planned |
| D2 | GET | /api/dashboard/expiring-soon | user | 200, 400, 401, 403 | 4 | planned |

Not in the API (by design): `/health/live`, `/health/ready` (operations, not part of the mobile contract) and `/openapi/v1.json`.

---

## 3. Shared schemas

```text
AuthResponse {
  accessToken: string                 // JWT
  accessTokenExpiresAt: timestamp
  refreshToken: string                // opaque; store in secure storage only
  refreshTokenExpiresAt: timestamp
  user: UserDto
  shop: ShopMembershipDto             // the active shop of this token
}

UserDto { id: uuid, email: string, fullName: string, phone: string|null, preferredLanguage: "ar"|"en" }

ShopMembershipDto {
  id: uuid, name: string,
  shopType: "Pharmacy"|"Grocery"|"MobileShop"|"Other",
  status: "Active"|"Suspended",
  role: "Owner"|"Staff"
}

UnitDto {
  id: uuid, name: string,
  factor: factor-string,              // base units in one of this unit; "1.000000" for the base unit
  sellPrice: money-string,
  barcode: string|null,
  isBase: boolean
}

ProductListItem {
  id: uuid, name: string,
  quantity: quantity-string,          // base unit; may be negative
  baseUnit: { id: uuid, name: string, sellPrice: money-string },
  lowStockThreshold: quantity-string,
  isLowStock: boolean,                // quantity <= lowStockThreshold
  isArchived: boolean
}

ProductDetail {
  id: uuid, name: string,
  barcode: string|null, category: string|null,
  quantity: quantity-string, costPrice: money-string,   // cost per base unit
  lowStockThreshold: quantity-string, isLowStock: boolean, isArchived: boolean,
  units: UnitDto[],                   // base unit first, then by factor ascending
  createdAt: timestamp, updatedAt: timestamp
}

StockMovementDto {
  id: uuid, productId: uuid,
  quantityChange: quantity-string,    // base unit, never "0.000"
  balanceAfter: quantity-string,
  reason: "InvoiceReceipt"|"Sale"|"ManualAdjustment"|"StockCount"|"InvoiceReversal",
  invoiceId: uuid|null, saleId: uuid|null,
  note: string|null,
  createdByUserId: uuid,
  createdAt: timestamp
}
```

---

## 4. Auth (step 1.5)

### A1 `POST /api/auth/register` (anonymous)
Creates the user, the shop, the Owner membership and the active subscription to the default plan, in one transaction.

Request:
```json
{
  "fullName": "Mona Adel",
  "email": "mona@example.com",
  "password": "Str0ngPass",
  "phone": "01001234567",
  "preferredLanguage": "ar",
  "deviceId": "pixel-7-abc",
  "shop": { "name": "صيدلية الشفاء", "shopType": "Pharmacy", "phone": null, "governorate": "القاهرة", "address": null }
}
```
- `email`: required, valid, max 256, case-insensitive unique. `password`: required, 8–128 chars, at least one letter and one digit. `fullName`: required, max 200. `phone`: optional, max 30. `preferredLanguage`: optional, `ar` (default) or `en`. `deviceId`: optional, max 100. `shop.name`: required, max 200, non-empty after Arabic normalization. `shop.shopType`: required.

Responses: `201` `AuthResponse` (role `Owner`). `400 Validation.Failed`, `400 Shop.NameRequired`. `409 Auth.EmailTaken`.

### A2 `POST /api/auth/login` (anonymous)
Request: `{ "email": string, "password": string, "deviceId": string|null }`

Responses: `200` `AuthResponse`. `401 Auth.InvalidCredentials` (same code for unknown email and wrong password). `403 Shop.Suspended`. `403 Auth.NoActiveShop` (user has no active membership).
If the user belongs to several shops, the token is for the active membership joined first; switching shops is not in v1.

### A3 `POST /api/auth/refresh` (anonymous)
Request: `{ "refreshToken": string }` → `200` `AuthResponse` with a **new** refresh token (the old one is revoked).
Errors: `401 Auth.InvalidRefreshToken` (unknown or expired), `401 Auth.RefreshTokenReused` (revoked token presented: the whole chain is revoked), `403 Shop.Suspended`.

### A4 `POST /api/auth/logout` (user)
Request: `{ "refreshToken": string }` → `204`. Revokes that refresh token and its chain. Unknown or already revoked token → still `204` (logout is idempotent). `400 Validation.Failed` when the field is missing.

### A5 `GET /api/auth/me` (user)
Used at app start-up to restore the session.
`200` `{ "user": UserDto, "shop": ShopMembershipDto }`: the user (including `preferredLanguage`), the active shop of the token and the user's `role` in it.

---

## 5. Products, units, stock (step 1.6; P12 in Sprint 4)

Roles: Owner and Staff can create and edit products and units; only the Owner archives, adjusts stock and records stock counts (roadmap assumptions).

### P1 `GET /api/products` (user)
Query: `search` (optional, max 100; matched against the normalized name, Arabic-normalized), `lowStock` (bool, default `false`: only `quantity <= lowStockThreshold`), `includeArchived` (bool, default `false`), `page`, `pageSize`.
Sorted by normalized name. `200` paged `ProductListItem`.
`lowStock=true` is served by `GetLowStockQuery` (partial index `ix_products_low_stock`).

### P2 `GET /api/products/{id}` (user)
`200` `ProductDetail`. `404 Product.NotFound` (also for another shop's product).

### P3 `POST /api/products` (user)
```json
{
  "name": "بنادول اكسترا",
  "barcode": "6221234567890",
  "category": "مسكنات",
  "baseUnitName": "شريط",
  "costPrice": "12.50",
  "baseUnitSellPrice": "15.00",
  "lowStockThreshold": "5.000",
  "openingQuantity": "24.000"
}
```
`barcode`, `category` optional (max 64 / 100); `lowStockThreshold` optional (default `"0.000"`).
`openingQuantity` optional (quantity in the base unit, `>= 0`, **Owner only**): when present and greater than zero, the handler records a `StockCount` movement with note `"Opening stock"` in the **same transaction** as the product, so every quantity has a movement behind it. Absent or `"0.000"`: the product starts at `"0.000"` with no movement. A Staff request that includes `openingQuantity` → `403 Product.OpeningStockOwnerOnly` (nothing is created).
`201` `ProductDetail`, header `Location: /api/products/{id}`.
Errors: `400 Product.NameRequired`, `ProductUnit.NameRequired`, `Precision.TooManyDecimals`, `Precision.Negative`, `Precision.OutOfRange`; `403 Product.OpeningStockOwnerOnly`; `409 Product.DuplicateBarcode` (barcode used by another non-archived product of the shop).

### P4 `PUT /api/products/{id}` (user)
`{ "name", "barcode", "category", "costPrice", "lowStockThreshold" }` (all required except `barcode`, `category`). Base-unit name and price are changed through P7.
`200` `ProductDetail`. Errors: `400` as P3, `404 Product.NotFound`, `409 Product.Archived`, `409 Product.DuplicateBarcode`.

### P5 `POST /api/products/{id}/archive` (owner)
`204`. Idempotent. `404 Product.NotFound`, `403 Auth.Forbidden` (Staff).

### P6 `POST /api/products/{id}/units` (user)
`{ "name": "علبة", "factor": "3.000000", "sellPrice": "40.00", "barcode": null }` → `201` `UnitDto`, `Location: /api/products/{id}/units/{unitId}`.
Errors: `400 ProductUnit.NameRequired`, `Precision.NotPositive` (factor), `Precision.TooManyDecimals`, `Precision.Negative`, `Precision.OutOfRange`; `404 Product.NotFound`; `409 Product.Archived`, `409 ProductUnit.DuplicateName`, `409 ProductUnit.DuplicateBarcode` (barcode used by any unit in the shop).

### P7 `PUT /api/products/{id}/units/{unitId}` (user)
Same body as P6 → `200` `UnitDto`. The base unit's factor must stay `"1.000000"`.
Errors: as P6, plus `400 ProductUnit.BaseUnitFactorFixed`, `404 ProductUnit.NotFound`.

### P8 `DELETE /api/products/{id}/units/{unitId}` (user)
`204`. Errors: `400 ProductUnit.BaseUnitCannotBeRemoved`, `404 Product.NotFound`, `404 ProductUnit.NotFound`, `409 Product.Archived`, `409 ProductUnit.InUse` (referenced by an invoice or sale line; Sprint 2+).

### P9 `POST /api/products/{id}/adjustments` (owner)
Manual correction (a new movement; nothing is edited, AGENTS.md rule 2).
```json
{ "quantityChange": "-2.000", "unitId": null, "note": "Expired, thrown away" }
```
`unitId` optional (default: base unit); the change is converted with `ProductUnit.ToBase`. `note` required, max 500.
`201` `StockMovementDto` (reason `ManualAdjustment`).
Errors: `400 Stock.ZeroChange`, `Stock.NoteRequired`, `Precision.TooManyDecimals`, `Precision.OutOfRange`; `404 Product.NotFound`, `404 ProductUnit.NotFound`.
Allowed on archived products (late correction).

### P10 `POST /api/products/{id}/stock-counts` (owner)
```json
{ "countedQuantity": "26.000", "unitId": null }
```
`200`:
```json
{ "changed": true, "quantity": "26.000", "movement": { "...": "StockMovementDto" } }
```
When the count equals the current quantity: `changed: false`, `movement: null` (no zero movements).
Errors: `400 Precision.Negative`, `Precision.TooManyDecimals`, `Precision.OutOfRange`; `404 Product.NotFound`, `404 ProductUnit.NotFound`.

### P11 `GET /api/products/{id}/movements` (user)
Query: `page`, `pageSize`. Newest first. `200` paged `StockMovementDto`. `404 Product.NotFound`.

### P12 `GET /api/products/lookup?barcode=...` (user, Sprint 4)
Finds a non-archived product by product barcode or unit barcode (quick sale).
`200` `{ "product": ProductListItem, "unit": UnitDto }` (`unit` = the unit whose barcode matched, or the base unit for a product barcode). `400 Validation.Failed` (missing barcode), `404 Product.NotFound`.

---

## 6. Invoices (Sprint 2; confirm in Sprint 4)

```text
InvoiceStatus: "Processing" | "NeedsReview" | "Confirmed" | "Failed"
InvoiceWarning: "TotalMismatch" | "ItemCapExceeded" | "UnreadableLines" | "DuplicateInvoiceNumber"

InvoiceListItem {
  id: uuid, status: InvoiceStatus,
  supplierName: string|null, invoiceNumber: string|null, invoiceDate: date|null,
  totalAmount: money-string|null, itemCount: int, warnings: InvoiceWarning[],
  createdAt: timestamp
}

InvoiceItemDto {
  id: uuid, lineNo: int, rawName: string,
  productId: uuid|null, productName: string|null,        // null = new product created on confirm
  productUnitId: uuid|null, unitName: string|null,
  quantity: quantity-string,                             // in the line's unit, > 0
  unitPrice: money-string, lineTotal: money-string,
  expiryDate: date|null, batchNo: string|null,           // informational (ADR-0004)
  matchConfidence: confidence-string|null
}

InvoiceDetail {
  id: uuid, clientRequestId: uuid, status: InvoiceStatus,
  supplierId: uuid|null, supplierName: string|null,
  invoiceNumber: string|null, invoiceDate: date|null,
  totalAmount: money-string|null, computedTotal: money-string|null,
  warnings: InvoiceWarning[], failureReason: string|null,
  items: InvoiceItemDto[],                               // by lineNo
  createdAt: timestamp, processedAt: timestamp|null, confirmedAt: timestamp|null
}
```

### I1 `POST /api/invoices/scan` (user)
`multipart/form-data`: `image` (file, `image/jpeg` | `image/png` | `image/webp`, max 8 MB), `clientRequestId` (UUID).
- New scan: `202` `InvoiceListItem` (status `Processing`), `Location: /api/invoices/{id}`. Processing runs in a background job.
- Repeat `clientRequestId`: `200` `InvoiceListItem` with the current status. Quota and rate limit are not consumed again.
Errors: `400 Invoice.ImageRequired`, `400 Invoice.UnsupportedImageType`, `400 Validation.Failed` (clientRequestId); `413 Invoice.ImageTooLarge`; `429 Scan.RateLimited` (header `Retry-After` seconds), `429 Scan.DailyQuotaExceeded` (resets at Africa/Cairo midnight).

### I2 `GET /api/invoices` (user)
Query: `status` (optional InvoiceStatus), `page`, `pageSize`. Newest first. `200` paged `InvoiceListItem`.

### I3 `GET /api/invoices/{id}` (user)
`200` `InvoiceDetail`. `404 Invoice.NotFound`. The app polls every 2 s for up to 60 s while `status == "Processing"` (served from the status cache).

### I4 `PUT /api/invoices/{id}/items` (user)
Replaces header fields and all lines while the invoice is `NeedsReview`.
```json
{
  "supplierName": "شركة الأدوية المتحدة",
  "invoiceNumber": "INV-1043",
  "invoiceDate": "2026-10-10",
  "totalAmount": "1250.00",
  "items": [
    {
      "lineNo": 1, "rawName": "بنادول اكسترا ٢٠ قرص",
      "productId": "uuid|null", "productUnitId": "uuid|null",
      "newProduct": null,
      "quantity": "10.000", "unitPrice": "120.00",
      "expiryDate": "2027-03-31", "batchNo": "B12"
    }
  ]
}
```
- Each line has either `productId` + `productUnitId` (existing product and one of its units), or `newProduct` (created on confirm):
  `newProduct { name: string, baseUnitName: string, sellPrice: money-string }` (stored in `invoice_items.new_product_name`, `new_product_base_unit_name`, `new_product_sell_price`). The new product's cost price is the line's `unitPrice`; the line quantity is in its base unit. Category is set later with P4.
- `lineNo` unique, 1..100 lines. `quantity > 0`, `unitPrice >= 0`. `lineTotal` is computed by the server.
`200` `InvoiceDetail` (warnings recomputed, including `TotalMismatch`).
Errors: `400 Validation.Failed`, `Precision.*`, `400 Invoice.LineProductRequired` (neither product nor newProduct), `400 Invoice.UnitNotOfProduct`; `404 Invoice.NotFound`, `404 Product.NotFound`, `404 ProductUnit.NotFound`; `409 Invoice.NotEditable` (status not NeedsReview).

### I5 `POST /api/invoices/{id}/confirm` (user, Sprint 4)
No body. Receives stock for every line in one transaction (lock order: invoice, then products by id), creates new products and aliases.
`200` `InvoiceDetail` (status `Confirmed`). Repeat: `200` with the same invoice, no new movements.
Errors: `400 Invoice.LineProductRequired`; `404 Invoice.NotFound`; `409 Invoice.NotConfirmable` (status `Processing` or `Failed`).

---

## 7. Sales (Sprint 4)

```text
SaleItemDto {
  id: uuid, productId: uuid, productName: string,
  productUnitId: uuid, unitName: string,
  quantity: quantity-string,
  listUnitPrice: money-string,         // the unit's sell price at sale time
  unitPrice: money-string,             // price charged
  priceOverridden: boolean,            // unitPrice != listUnitPrice
  lineTotal: money-string,             // quantity × unitPrice
  balanceAfter: quantity-string        // product quantity in base units after this line
}
SaleDto {
  id: uuid, clientRequestId: uuid, totalAmount: money-string, note: string|null,
  priceOverridden: boolean,            // any line overridden
  items: SaleItemDto[], createdAt: timestamp, createdByUserId: uuid,
  warnings: { code: "Stock.NegativeBalance", productId: uuid }[]
}
SaleListItem { id: uuid, totalAmount: money-string, itemCount: int, createdAt: timestamp }
```

### S1 `POST /api/sales` (user)
```json
{
  "clientRequestId": "uuid",
  "note": null,
  "items": [ { "productId": "uuid", "productUnitId": "uuid", "quantity": "2.000", "unitPrice": null } ]
}
```
- 1..100 items. `quantity > 0`. `unitPrice` optional: default is the unit's `sellPrice`. A different price is allowed (cashier decision); each line stores both `list_unit_price` (the unit's sell price at sale time) and `unit_price` (charged), and the response flags `priceOverridden`.
- Negative stock never blocks the sale; it is reported in `warnings` (and on the dashboard).
- New sale: `201` `SaleDto`, `Location: /api/sales/{id}`. Repeat `clientRequestId`: `200` existing `SaleDto`.
Errors: `400 Validation.Failed`, `Precision.*`, `400 Sale.ArchivedProduct`, `400 Sale.UnitNotOfProduct`; `404 Product.NotFound`, `404 ProductUnit.NotFound`.

### S2 `GET /api/sales` (user)
Query: `from`, `to` (optional timestamps, UTC), `page`, `pageSize`. Newest first. `200` paged `SaleListItem`.

### S3 `GET /api/sales/{id}` (user)
`200` `SaleDto`. `404 Sale.NotFound`.

---

## 8. Dashboard (Sprint 4)

### D1 `GET /api/dashboard/summary` (user)
Cached 2 minutes per shop (version key, `docs/design/redis.md`).
```json
{
  "productCount": 412,
  "lowStockCount": 9,
  "negativeStockCount": 2,
  "expiringSoonCount": 5,
  "todaySales": { "count": 37, "total": "4815.50", "overriddenSalesCount": 3 },
  "invoicesAwaitingReview": 1,
  "generatedAt": "2026-10-12T07:00:00Z"
}
```
"Today" is the Africa/Cairo calendar day. `overriddenSalesCount` = today's sales with at least one price-overridden line.

### D2 `GET /api/dashboard/expiring-soon` (user)
Query: `days` (int, default `60`, `1..365`), `page`, `pageSize`. Lines from confirmed invoices whose `expiryDate` is within `days` and whose product still has stock (ADR-0004: a hint, not an exact count). Soonest first.
```text
ExpiringItem { productId: uuid, productName: string, expiryDate: date, batchNo: string|null,
               invoiceId: uuid, lineQuantity: quantity-string, currentQuantity: quantity-string }
```
`200` paged `ExpiringItem`.

---

## 9. Error code catalogue

| Code | Status | Where | Meaning |
|---|---|---|---|
| `Validation.Failed` | 400 | all | Request shape or format invalid; see `errors` |
| `Server.Unexpected` | 500 | all | Unexpected error; no details |
| `Auth.Unauthenticated` | 401 | all authenticated | Missing, invalid or expired access token |
| `Auth.Forbidden` | 403 | owner-only | Role not allowed |
| `Auth.EmailTaken` | 409 | A1 | Email already registered |
| `Auth.InvalidCredentials` | 401 | A2 | Unknown email or wrong password |
| `Auth.NoActiveShop` | 403 | A2 | User has no active shop membership |
| `Auth.InvalidRefreshToken` | 401 | A3 | Unknown or expired refresh token |
| `Auth.RefreshTokenReused` | 401 | A3 | Revoked token reused; chain revoked |
| `Shop.NameRequired` | 400 | A1 | Shop name blank after normalization |
| `Shop.UnknownType` | 400 | A1 | Unsupported shop type |
| `Shop.Suspended` | 403 | all authenticated, A2, A3 | Shop suspended by the platform admin |
| `Product.NotFound` | 404 | P2–P12, I4, S1 | No such product in this shop |
| `Product.NameRequired` | 400 | P3, P4 | Name blank after normalization |
| `Product.Archived` | 409 | P4, P6–P8 | Archived products cannot be edited |
| `Product.DuplicateBarcode` | 409 | P3, P4 | Barcode used by another active product |
| `Product.OpeningStockOwnerOnly` | 403 | P3 | Only the Owner may set `openingQuantity` |
| `ProductUnit.NotFound` | 404 | P7–P10, I4, S1 | No such unit on this product |
| `ProductUnit.NameRequired` | 400 | P3, P6, P7 | Unit name blank |
| `ProductUnit.DuplicateName` | 409 | P6, P7 | Unit name already used on the product |
| `ProductUnit.DuplicateBarcode` | 409 | P6, P7 | Unit barcode already used in the shop |
| `ProductUnit.BaseUnitCannotBeRemoved` | 400 | P8 | |
| `ProductUnit.BaseUnitFactorFixed` | 400 | P7 | Base unit factor is always 1 |
| `ProductUnit.InUse` | 409 | P8 | Unit referenced by invoice or sale lines |
| `Precision.TooManyDecimals` | 400 | money/quantity/factor inputs | |
| `Precision.Negative` | 400 | money, threshold, count | |
| `Precision.NotPositive` | 400 | factor | |
| `Precision.OutOfRange` | 400 | money/quantity/factor inputs | Value does not fit the column |
| `Stock.ZeroChange` | 400 | P9 | |
| `Stock.NoteRequired` | 400 | P9 | |
| `Invoice.NotFound` | 404 | I3–I5 | |
| `Invoice.ImageRequired` | 400 | I1 | |
| `Invoice.UnsupportedImageType` | 400 | I1 | |
| `Invoice.ImageTooLarge` | 413 | I1 | Over 8 MB |
| `Invoice.NotEditable` | 409 | I4 | Not in NeedsReview |
| `Invoice.NotConfirmable` | 409 | I5 | Processing or Failed |
| `Invoice.LineProductRequired` | 400 | I4, I5 | Line has neither product nor newProduct |
| `Invoice.UnitNotOfProduct` | 400 | I4 | Unit belongs to another product |
| `Scan.RateLimited` | 429 | I1 | Per-minute limit; `Retry-After` header |
| `Scan.DailyQuotaExceeded` | 429 | I1 | Plan's daily scan quota used up |
| `Sale.NotFound` | 404 | S3 | |
| `Sale.ArchivedProduct` | 400 | S1 | Archived products cannot be sold |
| `Sale.UnitNotOfProduct` | 400 | S1 | Unit belongs to another product |
| `Stock.NegativeBalance` | — | S1 warnings | Not an error: sale succeeded, stock is negative |

Domain movement-rule codes (`Stock.InvoiceSourceRequired`, `Stock.SaleSourceRequired`, `Stock.SourceNotAllowed`, `Stock.EmptySourceReference`, `Stock.ChangeMustBePositive`, `Stock.ChangeMustBeNegative`, `Stock.UserRequired`, `Stock.UnknownReason`) protect server-built movements; a client request cannot trigger them. If one ever reaches the API it is a server bug and is returned as `500 Server.Unexpected`.

---

## 10. Decisions taken in this version (for review)

1. Base path `/api` without a version segment; the contract version is this document's version.
2. Enums are PascalCase strings.
3. Money, quantities, factors and confidence are decimal strings with fixed decimals in responses; JSON numbers are rejected for them.
4. `pageSize > 100` is rejected (400), not clamped.
5. Password policy: 8–128 chars, at least one letter and one digit.
6. Login with several memberships picks the active membership joined first; no shop switching in v1.
7. Logout is idempotent (204 even for an unknown token).
8. `GET /api/auth/me` added for app start-up.
9. (v1.1) Optional `openingQuantity` on P3, Owner only: a `StockCount` movement with note "Opening stock" in the same transaction. Staff → `403 Product.OpeningStockOwnerOnly`.
10. Adjustments and counts accept an optional `unitId` and convert with `ProductUnit.ToBase`.
11. `GET /api/products/{id}/movements` added (served by `stock_movements (shop_id, product_id, created_at DESC)`).
12. Barcode lookup (P12) searches product and unit barcodes of non-archived products.
13. (v1.1) New products from invoices (I4 `newProduct`): stored in `invoice_items.new_product_name`, `new_product_base_unit_name`, `new_product_sell_price`; cost = line `unitPrice`. A line references an existing product or describes a new one, never both; a confirmed line never has neither. Specified in `docs/design/backend.md` (Sprint 2).
14. (v1.1) Sale `unitPrice` may differ from the unit's sell price. `sale_items` stores `list_unit_price` and `unit_price`; responses expose `priceOverridden` per line and per sale; D1 reports `overriddenSalesCount` for today.
15. Negative stock is a warning in the sale response, never an error.
16. `ProductUnit.InUse` (409) protects history: a unit referenced by invoice or sale lines cannot be deleted.
17. Domain-only movement codes surface as 500 (server bug), never as client errors.
