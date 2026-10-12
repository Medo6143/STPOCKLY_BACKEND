Read AGENTS.md fully before starting.

# Brief: docs-sprint2-backend-design

Repository: `stockly-backend` (`D:\FLUTTER\stockly`) · Lane: DOCS · Contract version: **v1.1** (`docs/api/contract.md`, approved)

You have not seen the conversation that produced this brief. Everything you need is here or in the files it links. If something you need is missing or contradicts AGENTS.md, an ADR or the contract, **stop and report** instead of guessing.

## 1. Goal and why
Sprint 2 builds the AI invoice-scan pipeline: a photo uploaded to `POST /api/invoices/scan` becomes a review-ready invoice through a Hangfire job that calls OpenRouter. Before any Sprint 2 code is written, the design must be fixed in one document so implementation briefs can be exact. Write `docs/design/backend.md`: a complete, implementable design for Sprint 2, plus the Sprint 4 confirm transaction it must fit. You write documentation only; no code.

## 2. Scope
**You may change:**
- `docs/design/backend.md` (new file).
- `docs/erd.md`: only the `invoice_items` parts (the mermaid `invoice_items` block and its section in §3.3), and §3.7 "Migration status" to list the planned Sprint 2 migration.

**You must NOT touch:**
- Anything else, in particular `src/`, `tests/`, `docs/api/`, `docs/adr/`, `AGENTS.md`, `docs/agents/`, `docs/sprints/`.
- Do not run `dotnet build`, `dotnet test`, `docker`, or any `git` command that changes state. Reading files is fine.

## 3. Critical rules for this task
Full rules: `AGENTS.md`. Copied here because the design must obey them:
- 3. Confirm invoice and create sale are idempotent: status check + row lock + unique indexes on (invoice_item_id, reason) and (sale_item_id).
- 4. Lock order is always: invoice (or sale) first, then products ordered by `Id`.
- 12. Never log secrets, tokens, passwords, full images or raw AI JSON. Log IDs and sizes.
- 14. OpenRouter is called only from `Infrastructure/Ai`, only inside the Hangfire job, never inside an HTTP request.
- 15. AI output is untrusted: strict DTO, FluentValidation, max 100 items, raw JSON stored in `Invoice.RawAiJson`.
- 16. If `|sum(lines) − total| > 2% of total`, status is `NeedsReview` with warning `TotalMismatch`.
- 17. `temperature = 0`, `response_format = json_object`, model and fallback model from config, timeout 30 s, retry max 2 with backoff on 429/5xx only.
- 18. Every call (including retries and cache hits) writes an `AiUsageRecord`.
- 19. The AI never changes stock. Only a user confirmation does.
- 20. `POST /invoices/scan` and `POST /sales` require a client-generated `ClientRequestId`. A repeat returns the existing resource (ADR-0006).
- 21–25. Cache access only through `ICacheService`, `IAiResultCache`, `IInvoiceStatusCache`, `IScanRateLimiter`, `IScanQuotaCounter`; tenant keys start with `stockly:{shopId}:`; invalidate after commit; never read a quantity from cache for a write; Redis failure degrades to Postgres.
- 31–32. Schema changes only through EF migrations; every new filter or sort ships with its index, documented in `docs/erd.md`.
- 42. No `DateTime.Now`/`UtcNow` (use `IClock`), no `new HttpClient()` (use `IHttpClientFactory`).
- SOLID "O": a new invoice check is a new `IInvoiceValidationRule` class registered in DI; no growing `switch`.

Also read: `docs/erd.md` (§3.3 invoices, §3.7 migration status, §4 confirm transaction), `docs/design/redis.md` (AI cache, status cache, rate limit, quota, failure behaviour), ADR-0001, 0003, 0004, 0006, 0007, 0008, `docs/roadmap.md` (Sprint 2 scope), `docs/architecture/structure.md` (where each class lives), and the existing code under `src/` for naming (Domain `Result`/`Error`, `Product`, `ProductUnit`, `Product.RecordMovement`, `ArabicTextNormalizer`).

## 4. API contract (relevant sections, v1.1, binding)

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


## 5. What `docs/design/backend.md` must contain
1. **Overview** of the Sprint 2 pipeline and which project and class owns each step (match `docs/architecture/structure.md`).
2. **Mermaid sequence diagrams**:
   a. Scan: client → `POST /api/invoices/scan` (validation, `clientRequestId` idempotency, rate limit, daily quota, MinIO upload, 202) → Hangfire `ProcessInvoiceJob` → storage → AI result cache (sha256) → OpenRouter (with fallback model) → validate → match → save → status cache.
   b. Polling `GET /api/invoices/{id}` with `IInvoiceStatusCache` and its fallback to Postgres.
   c. Confirm (Sprint 4): transaction, lock order (invoice, then products by id), idempotency, new products from `newProduct` lines, aliases, stock movements, cache bump after commit.
3. **OpenRouter**: request DTO and response DTO (C# records, exact JSON field names), the **full system prompt** (handles Arabic and English invoices, returns `null` for unreadable fields, strict JSON schema, units as written, max 100 items), the JSON schema of the expected output, model and fallback-model configuration keys, token and cost capture for `AiUsageRecord`.
4. **Resilience**: the exact `Microsoft.Extensions.Http.Resilience` pipeline (timeout 30 s, retry max 2 with backoff on 429/5xx only, then fallback model) and what is recorded per attempt.
5. **Validation**: the list of `IInvoiceValidationRule` classes (one per rule: item cap, totals tolerance 2%, unreadable lines, duplicate invoice number, ...) with input, output (warning code or status) and order.
6. **Matching v1**: exact alias match (`product_aliases`, supplier-scoped), then exact normalized-name match; confidence values; what is left for Sprint 6 (trigram).
7. **#13 — `invoice_items` columns for new products** (approved decision, binding):
   - Columns `new_product_name text`, `new_product_base_unit_name text`, `new_product_sell_price numeric(18,2)`.
   - Rule: a line either references an existing product (`product_id` set) or describes a new one (the `new_product_*` fields set); **never both**, and a **confirmed** line **never has neither**.
   - Specify the exact CHECK constraint(s) and any supporting column(s) needed to enforce this in the database, how the rule holds at each status (`NeedsReview` lines straight from the AI may have neither), and what happens on confirm when the new product is created (does the line then point to the new product, and how does that stay compatible with "never both"?). If the rule as stated cannot be enforced exactly, **stop and report** the conflict with your proposed options instead of changing the rule.
   - Update `docs/erd.md` (`invoice_items` mermaid block and the §3.3 table with each new constraint and the rule it serves).
8. **Migration plan for Sprint 2**: tables (`suppliers`, `product_aliases`, `invoices`, `invoice_items`, `ai_usage_records`), every index from `docs/erd.md`, the deferred FKs from `docs/erd.md` §3.7 that become real (`stock_movements.invoice_id`, `invoice_item_id`), and the Hangfire schema.
9. **Error codes** used, matching the contract catalogue (`Invoice.*`, `Scan.*`). Do not invent new public codes; if you need one, list it under open questions.
10. **Configuration keys** (names only, for example `OpenRouter__Model`), which ones are secrets, and the `.env.example` additions as a list (do not edit `.env.example`).
11. **Open questions** you could not resolve from the docs.

Keep it concrete: class names, method signatures where they matter, column types, index names. No code beyond short signatures, DTO records and the prompt text.

## 6. Checks you must pass before finishing
- Every Mermaid block is valid Mermaid syntax (`sequenceDiagram` / `erDiagram`).
- Every endpoint, field and error code you mention matches contract v1.1 exactly.
- Every rule in section 3 is respected by the design; say where.
- `git status --short` shows changes only in `docs/design/backend.md` and `docs/erd.md`.

## 7. Do not commit, push or merge.
Leave your changes in the working tree. The orchestrator reviews and commits.

## 8. Final report (exact format)
```
AGENTS.md read: yes
Rules applied: <numbers and where in the design>
Files changed: <list>
Check outputs: <git status --short>
Decisions taken: <list, with reason>
Open questions: <list or "none">
Contract deviations: NONE   (anything else: stop before finishing and report it)
```
