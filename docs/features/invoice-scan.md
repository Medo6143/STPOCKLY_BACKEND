# Feature: Invoice scan

Status: Sprint 2 scope complete (step 2a: upload, background extraction; step 2b: product matching, I2/I3/I4 review and reads). Confirm (I5) is Sprint 4 · Sprint: 2 · Owner: medo6143

## 1. Purpose
A shop owner photographs a supplier invoice instead of typing it. The API stores the photo, a background job reads it with an AI vision model (OpenRouter) and saves the lines, and the owner reviews them before anything touches stock. The AI never changes stock (AGENTS.md rule 19): only a human confirmation (Sprint 4) does.

## 2. User flow
1. **مسح فاتورة** (Scan invoice): the app takes a photo (JPEG, PNG or WebP, at most 8 MB), creates a `clientRequestId` once, and uploads it (I1). Offline, the upload waits in the queue and is retried with the same `clientRequestId` (ADR-0006).
2. The API answers at once with `202` and status **جاري القراءة** (`Processing`); a retry of the same upload answers `200` with the same invoice.
3. The app polls the invoice (I3) every 2 s for up to 60 s. While it is `Processing` the answer comes from the Redis status snapshot. The job turns it into **بانتظار المراجعة** (`NeedsReview`, with lines, matched products and warnings) or **فشلت القراءة** (`Failed`, the user scans again).
4. **مراجعة الفاتورة** (Review): each line shows the matched product and unit with its confidence, or nothing. The user picks an existing product and unit, or describes a new product (name, base unit, sell price), fixes quantities, prices and the header, and saves (I4). The answer is the whole invoice with recomputed warnings. Nothing touches stock.
5. **الفواتير** (Invoices list, I2): newest first, optionally only one status (for example "waiting for review").
6. Confirm (I5): Sprint 4.

## 3. Endpoints
Contract v1.5 §6 (`docs/api/contract.md`; §6 unchanged since v1.4); machine-readable in `docs/api/openapi.json`.

| # | Method | Path | Auth / role | Request | Response | Status codes |
|---|---|---|---|---|---|---|
| I1 | POST | `/api/invoices/scan` | user | `multipart/form-data`: `image` (file), `clientRequestId` (UUID) | new: `202 InvoiceListItem` (status `Processing`, `itemCount` 0), `Location: /api/invoices/{id}`; repeat: `200 InvoiceListItem` | 200, 202, 400, 401, 403, 413, 429 |
| I2 | GET | `/api/invoices` | user | query `status` (optional `InvoiceStatus`), `page`, `pageSize` (1..100) | `200` paged `InvoiceListItem`, newest first | 200, 400, 401, 403 |
| I3 | GET | `/api/invoices/{id}` | user | — | `200 InvoiceDetail` (lines by `lineNo`) | 200, 401, 403, 404 |
| I4 | PUT | `/api/invoices/{id}/items` | user | `UpdateInvoiceItemsRequest`: header fields and 1..100 lines, each `productId` + `productUnitId` or `newProduct` | `200 InvoiceDetail` (warnings recomputed) | 200, 400, 401, 403, 404, 409 |

I5 (confirm) comes in Sprint 4.

## 4. Data model changes
Step 2b adds no migration: matching and the reads use indexes of `InitialCatalog` and `InvoiceScan` (`docs/erd.md` §3.2 and §3.3 name the query each one serves). Migration **`InvoiceScan`** (`src/Stockly.Infrastructure/Persistence/Migrations/20261017180927_InvoiceScan.cs`, design: `docs/design/backend.md` §12). Every table is a tenant table (`shop_id` + query filter). Details and the query each index serves: `docs/erd.md` §3.2, §3.3, §3.6, §3.7.

- `suppliers`, `product_aliases` (used by matching and confirm; the scan job only looks suppliers up by normalized name).
- `invoices`: status, image key and SHA-256, header fields, `computed_total`, `warnings` (jsonb array of names), `raw_ai_json` (jsonb), `failure_reason`, `processed_at`. Indexes: `ux_invoices_shop_id_client_request_id` (idempotency), `ix_invoices_shop_id_created_at` (history, quota fallback count), `ix_invoices_shop_id_status_created_at` (status filter), `ix_invoices_shop_id_image_sha256` (reuse of an earlier extraction), `ix_invoices_shop_id_supplier_id_invoice_number` (duplicate number warning), `ix_invoices_status_created_at_failed` (admin).
- `invoice_items` with the nine `ck_invoice_items_*` constraints of backend.md §10.3 and `ux_invoice_items_invoice_id_line_no`.
- `ai_usage_records`: one row per AI attempt and per cache hit (rule 18), cost in USD `numeric(14,8)`.
- FKs `stock_movements.invoice_id` and `invoice_item_id` became real.
- Schema `hangfire` (Hangfire.PostgreSql creates its tables there at startup).

## 5. Integrations

**OpenRouter** (`Infrastructure/Ai/OpenRouter`)

| | |
|---|---|
| Why | Read the invoice photo into structured lines |
| Endpoint / operation | `POST {OpenRouter:BaseUrl}chat/completions`; `temperature` 0, `response_format` `json_object`, `max_tokens` 8000, `usage.include` true; image as a `data:` URL; prompt `invoice-extraction-v1` (backend.md §5.4) |
| Auth | `Authorization: Bearer {OpenRouter:ApiKey}` (secret, `.env` only, never logged); `X-Title`, optional `HTTP-Referer` |
| Timeout / retry | 30 s per attempt; at most 2 retries with exponential backoff and jitter (honours `Retry-After`, at most 10 s) on 429 and 5xx only; then the fallback model. Timeouts, network errors and other 4xx go straight to the fallback; 401/402/403 stop. At most 6 attempts per scan |
| Failure behaviour | Output is untrusted: strict parse (`AiJson`) and `ExtractedInvoiceValidator` (max 100 items). Both models failing → `Failed` with `AiUnavailable`, `AiOutputInvalid` or `AiNoItems` |
| Cost | Every attempt (and every cache hit, cost 0) is an `ai_usage_records` row with tokens, USD cost and latency |

**Hangfire** (`Infrastructure/Jobs`)

| | |
|---|---|
| Why | Run the extraction outside the HTTP request (rule 14) |
| Endpoint / operation | `ProcessInvoiceJob.RunAsync(shopId, invoiceId)` on queue `invoices`; server inside the Api process (`HangfireServerHost`), `Hangfire:WorkerCount` workers; PostgreSQL storage in schema `hangfire` with LISTEN/NOTIFY |
| Auth | Job arguments are ids only; the job sets its tenant (`JobTenantContext`) so the tenant filter applies |
| Timeout / retry | 3 automatic retries on unexpected exceptions; a retry normally hits the AI cache (no second paid call) |
| Failure behaviour | After the last retry `InvoiceJobFailureFilter` queues `FailInvoiceProcessingJob`, which marks the invoice `Failed` with `Unexpected`. The job is idempotent (status check + `FOR UPDATE` lock). The server waits for PostgreSQL in the background, so the Api starts while the database is down. A scan whose enqueue was lost is queued again when the app repeats it after `Scan:RequeueAfterSeconds` (600 s) |
| Cost | none |

**MinIO / S3** (`Infrastructure/Storage`)

| | |
|---|---|
| Why | Keep the invoice photo (ADR-0008) |
| Endpoint / operation | `PutObject` / `GetObject` / `DeleteObject` on `Storage:Bucket`, key `invoices/{shopId}/{yyyy}/{MM}/{invoiceId}.jpg` (real Content-Type on the object) |
| Auth | Least-privilege API user (`Storage:AccessKey`/`SecretKey`, `.env`); private bucket |
| Timeout / retry | AWS SDK defaults |
| Failure behaviour | Upload failure → 500, quota given back, no invoice. Missing object in the job → `Failed` with `ImageMissing`, no AI call |
| Cost | storage only |

**Redis** (`Infrastructure/Caching`)

| | |
|---|---|
| Why | Per-shop rate limit and daily quota; AI result cache; status snapshot for polling |
| Endpoint / operation | Lua `INCR`+`EXPIRE` counters; `GET`/`SET` with TTL (section 6) |
| Auth | `ConnectionStrings__Redis` |
| Timeout / retry | 250 ms per operation, no retry |
| Failure behaviour | Never fails a request (rule 25): rate limit allows; quota counts today's invoices in PostgreSQL; caches miss and skip writes; one throttled warning per minute |
| Cost | none |

## 6. Cache keys
| Key | TTL | Invalidated by |
|---|---|---|
| `stockly:{shopId}:rl:scan:{yyyyMMddHHmm}` (UTC minute) | 120 s | expires |
| `stockly:{shopId}:quota:scan:{yyyyMMdd}` (Cairo date) | 26 h | expires; a scan that loses a concurrent repeat or fails to upload gives its unit back |
| `stockly:{shopId}:ai:invoice:{sha256}` | 7 days | never (content-addressed); a prompt version change makes old values misses |
| `stockly:{shopId}:invoice:{id}:status` | 10 min | overwritten after every status change (scan, job, failure job); also written read-through by I3 when PostgreSQL shows `Processing`. I3 answers from it only while it says `Processing` |

`docs/design/redis.md` §1 is updated.

## 7. Business rules
- `clientRequestId` is required and unique per shop; a repeat returns the existing invoice with `200` and consumes neither the rate limit nor the quota (rule 20, ADR-0006). Another shop using the same id gets its own invoice.
- Image checks, in order: present (`Invoice.ImageRequired`), at most 8 MB (`413 Invoice.ImageTooLarge`, also for a request body over 9 MB), declared type in JPEG/PNG/WebP **and** matching magic bytes (`Invoice.UnsupportedImageType`).
- Rate limit before quota: at most `Scan:RateLimitPerMinute` (10) scans per shop per minute; a rate-limited scan never burns quota.
- Daily quota = the active plan's `daily_scan_quota`, per Africa/Cairo day; no active subscription means quota 0.
- The image is uploaded before the insert; no database transaction is open during the upload.
- Job order: image → earlier extraction of the same photo in this shop (database) → Redis AI cache → OpenRouter (primary, then fallback). Earlier and cached outputs are validated again before use.
- A line is kept only when its name, quantity (> 0, ≤ 3 decimals) and unit price (≤ 2 decimals, or exactly `lineTotal / quantity`) are readable; nothing is rounded except `line_total = round(quantity × unit_price, 2, away from zero)`. No readable line → `Failed` with `AiNoItems`.
- Warnings (`IInvoiceValidationRule`, one class each): `ItemCapExceeded` (model hit 100 lines), `UnreadableLines`, `TotalMismatch` (`|computed − total| > 2% of total`, rule 16), `DuplicateInvoiceNumber` (same number and supplier on another non-failed invoice). Warnings never change the status: a successful extraction is always `NeedsReview`.
- The supplier is linked when a non-archived supplier has the same normalized name; the job never creates suppliers.
- `raw_ai_json` keeps the output used (or the last seen); non-JSON output is stored as a JSON string literal, at most 256 KB.
- The job writes only `invoices`, `invoice_items` and `ai_usage_records`, in one transaction that locks the invoice and re-checks `Processing`; usage rows are saved even when another run finished first.

**Matching v1** (`Features/Invoices/Matching`, backend.md §8), run by the job after the supplier lookup:
- `IProductMatcher` runs the `IProductMatchStrategy` classes by order on the lines still unmatched; each strategy makes **one** query for all its lines (`= ANY(@names)`), never one per line. Archived products never match. A new strategy is a new class (SOLID O); trigram matching is Sprint 6.
- Order 10, `ExactAliasMatchStrategy`: the supplier's learned alias (`product_aliases`, written by confirm in Sprint 4) with exactly the line's normalized name; an unknown supplier uses only aliases without a supplier. Alias unit, confidence **1.00**.
- Order 20, `ExactNameMatchStrategy`: the one non-archived product whose normalized name equals the line's (two or more: ambiguous, no match). The unit whose normalized name equals the line's unit text, or the only unit when the line has none: **0.90**; otherwise the base unit: **0.70**.
- Nothing found: `productId`, `productUnitId`, `matchConfidence` stay null. Names and units are compared after `ArabicTextNormalizer` (hamza, teh marbuta, tashkeel, digits, case).

**Review edit (I4)** (backend.md §9.1):
- Shape (`400 Validation.Failed`): 1..100 lines, `lineNo` 1..100 and unique, `rawName` not blank, decimal strings, `quantity > 0`, `productId` and `productUnitId` together, never both a product and `newProduct`, `newProduct.name` and `baseUnitName` not blank. Precision (`Precision.*`): quantity at most 3 decimals, money at most 2 decimals and not negative.
- A line with neither a product nor `newProduct`: `400 Invoice.LineProductRequired`.
- The invoice is locked (`FOR UPDATE`; rule 4: only the invoice). Missing or another shop's: `404 Invoice.NotFound`; not `NeedsReview`: `409 Invoice.NotEditable`.
- Products and units are checked in one query: missing or another shop's product: `404 Product.NotFound`; unit that exists nowhere in the shop: `404 ProductUnit.NotFound`; unit of another product: `400 Invoice.UnitNotOfProduct`. An archived product is accepted only on the line number that already had it (§17.1 item 11); choosing it anywhere else: `404 Product.NotFound`.
- Every line is replaced (old rows deleted, new rows inserted, `line_total` and `computed_total` recomputed); the supplier is re-resolved by exact normalized name. `matchConfidence` is kept only when line number, product and unit are unchanged.
- Only the Review-stage warnings (`TotalMismatch`, `DuplicateInvoiceNumber`) are computed, and they replace the old list. No stock change (rule 19).

**Reads**:
- I3 is one SQL statement (invoice, lines by `line_no`, product and unit names). A confirmed new-product line (Sprint 4) shows the created product as `productId`/`productName` and keeps `newProduct`. A `Processing` invoice in the status cache is answered from the cache with no PostgreSQL query; Redis down: PostgreSQL. Another shop's id: `404 Invoice.NotFound`.
- I2 is paged (`pageSize` at most 100, larger is `400`), `ORDER BY created_at DESC`, optional `status`; `itemCount` is counted in the same statement.

## 8. Error cases
| Case | Error code | HTTP | User message (ar / en) |
|---|---|---|---|
| No image | `Invoice.ImageRequired` | 400 | أرفق صورة الفاتورة. / Attach a photo of the invoice. |
| Not JPEG/PNG/WebP, or bytes disagree | `Invoice.UnsupportedImageType` | 400 | يجب أن تكون الصورة بصيغة JPEG أو PNG أو WebP. / The image must be a JPEG, PNG or WebP photo. |
| Missing or invalid `clientRequestId` | `Validation.Failed` (`errors.clientRequestId`) | 400 | localized validation message |
| Image over 8 MB, body over 9 MB | `Invoice.ImageTooLarge` | 413 | حجم الصورة أكبر من 8 ميجابايت... / The image is larger than 8 MB... |
| Over 10 scans this minute | `Scan.RateLimited` + `Retry-After` (seconds to the next minute) | 429 | تم مسح فواتير كثيرة في هذه الدقيقة... / Too many invoices were scanned this minute... |
| Daily quota used up | `Scan.DailyQuotaExceeded` + `Retry-After` (seconds to Cairo midnight) | 429 | انتهت عمليات مسح الفواتير المتاحة في باقتك اليوم... / Your plan's invoice scans for today are used up... |
| Unknown `status`, `page` < 1, `pageSize` outside 1..100 (I2) | `Validation.Failed` | 400 | localized validation message |
| I4 shape (see §7) | `Validation.Failed` (`errors["items[i].field"]`) | 400 | localized validation message |
| I4 quantity or money with too many decimals, negative or too large | `Precision.TooManyDecimals` / `Precision.Negative` / `Precision.OutOfRange` | 400 | localized |
| I4 line without product or new product | `Invoice.LineProductRequired` | 400 | اختر منتجاً موجوداً أو أدخل بيانات منتج جديد لكل بند. / Choose an existing product or describe a new product for every line. |
| I4 unit of another product | `Invoice.UnitNotOfProduct` | 400 | الوحدة المختارة تخص منتجاً آخر. / The chosen unit belongs to another product. |
| I3/I4 invoice missing or another shop's | `Invoice.NotFound` | 404 | لم يتم العثور على الفاتورة. / The invoice was not found. |
| I4 product missing, another shop's, or archived on a new line | `Product.NotFound` | 404 | existing product text |
| I4 unit missing | `ProductUnit.NotFound` | 404 | existing unit text |
| I4 invoice not waiting for review | `Invoice.NotEditable` | 409 | يمكن تعديل الفاتورة فقط وهي بانتظار المراجعة. / Only an invoice waiting for review can be edited. |
| Job outcomes | `failure_reason`: `ImageMissing`, `AiUnavailable`, `AiOutputInvalid`, `AiNoItems`, `Unexpected` (not error codes; the app shows a generic message) | — | — |

## 9. Configuration / env vars
| Name | Example | Secret? |
|---|---|---|
| `OpenRouter__ApiKey` | `sk-or-...` | yes |
| `OpenRouter__BaseUrl` | `https://openrouter.ai/api/v1/` | no |
| `OpenRouter__Model` / `OpenRouter__FallbackModel` | `google/gemini-2.5-flash` / `openai/gpt-4.1-mini` (check availability and price with a real key before production) | no |
| `OpenRouter__TimeoutSeconds` / `__MaxRetries` / `__MaxOutputTokens` | `30` / `2` (0..2) / `8000` | no |
| `OpenRouter__AppUrl` / `__AppTitle` | empty / `Stockly` | no |
| `Storage__Endpoint` / `__Bucket` / `__Region` / `__ForcePathStyle` | `http://127.0.0.1:9000` / `stockly-invoices` / `us-east-1` / `true` | no |
| `Storage__AccessKey` / `Storage__SecretKey` | API user of `minio-init` | yes |
| `Scan__RateLimitPerMinute` / `Scan__RequeueAfterSeconds` | `10` / `600` | no |
| `Hangfire__WorkerCount` | `2` | no |

Every section is validated at startup; the error names the variable (`Set the environment variable OpenRouter__ApiKey ...`), never its value.

## 10. Mobile / Admin screens
Mobile (stockly-mobile): camera and upload queue, then the polling state. States: loading (uploading/`Processing`), error (400/413/429 messages above; `Failed` → "scan again"), data (`NeedsReview` → review screen: lines with matched product, unit and confidence; product picker or new-product form per line; save with I4; invoice list with I2 and a status filter). RTL: Arabic text stays as the model read it. Admin: none in this step (AI cost and failed scans later).

## 11. Tests
- Domain: `InvoiceTests` (create, complete, fail, terminal states), `TotalsTolerancePolicyTests`, `InvoiceItemTests` (line total rounding), `AiUsageRecordTests`, `SupplierTests`.
- Application: `ScanInvoiceCommandHandlerTests` (happy path, image/type/size, repeat, stale requeue, rate limit, quota, Redis-down fallback, upload failure, concurrent repeat, other shop, no tenant), `ScanInvoiceValidationTests`, `ProcessInvoiceCommandHandlerTests` (success, DB reuse, AI cache hit, AI failures, unreadable lines, supplier and duplicate number, image missing, not processing, concurrent finish, other shop, usage rows per path), `ExtractedInvoiceNormalizerTests`, `ExtractedInvoiceValidatorTests`, one test class per rule plus `InvoiceValidatorTests`, `FailInvoiceProcessingCommandHandlerTests`, `InvoiceSupportTests`.
- Infrastructure: `OpenRouterInvoiceExtractorTests` (fake `HttpMessageHandler`: valid, malformed, empty, too many items, 429, 5xx, timeout, network error, 400, 401/402/403, fallback, logs), `RedisScanLimiterTests` and `RedisInvoiceCachesTests` (Testcontainers Redis and Redis down), `S3FileStorageTests` (Testcontainers MinIO), `InvoicePersistenceTests` (PostgreSQL: idempotency index, reuse, counts, duplicates, tenant isolation, CHECK constraints), `ProcessInvoiceJobTests` (end to end on PostgreSQL, idempotent rerun, reuse, failures, Hangfire enqueue in schema `hangfire`), `MigrationTests`.
- Step 2b. Domain: `InvoiceTests` (matched lines, confidence guard, `ReplaceLines`: header, lines and warnings, confidence kept or cleared, every invalid case, not editable). Application: `ProductMatchingTests` (alias hit, name hit with unit found, single unit, unit missing or several units: base unit 0.70, ambiguous name, Arabic normalization, one query with distinct names, matcher ordering and skipping), `ProcessInvoiceCommandHandlerTests` (lines saved matched), `ExtractedInvoiceNormalizerTests` (match inputs), `UpdateInvoiceItemsCommandHandlerTests` (happy path, precision errors, `LineProductRequired`, not found, wrong tenant, `NotEditable`, product and unit not found, `UnitNotOfProduct`, archived product kept or refused, confidence kept or cleared, 100 lines), `UpdateInvoiceItemsValidatorTests` (every shape rule, 101 lines, duplicate numbers, both product and new product), `InvoiceReadHandlerTests` (cache hit with no PostgreSQL query, read-through, Redis down, wrong tenant, I2 filter and paging validation). Infrastructure: `ProductMatchingPersistenceTests` (PostgreSQL: **one statement per strategy** for seven lines, archived and ambiguous excluded, unknown supplier, other shop), `InvoiceReviewPersistenceTests` (I3 in one statement with names, confirmed new-product line, other shop; I2 paging, ordering, item count in two statements, status filter; I4 twice through MediatR with reused line numbers, no stock movement), `ProcessInvoiceJobTests` (matched line saved). Api: `InvoiceReviewEndpointTests` (scan, job, `NeedsReview` with matched lines, I3, I4, I3 with the Hangfire server and a fake AI; I3 while processing, Redis down, wrong tenant 404; I2 paging, filter, other shop, bad query 400; I4 409, 404 for another shop, every error code, JSON number rejected).
- Api: `ScanEndpointTests` (202/200, other shop, 400s, 413 for image and body, 429 rate limit and quota with `Retry-After`, Redis down, 401, end to end with the Hangfire server and a fake AI).

## 12. Known limitations
- Matching is exact only (alias text or normalized name); fuzzy and cross-script matching (trigram) is Sprint 6. Aliases are learned by confirm (Sprint 4), so until then only the name strategy finds products.
- I4 does not write the status snapshot: it never changes the status.
- An orphan image (upload succeeded, insert failed) is left for the bucket lifecycle rule (90 days, ADR-0008).
- Model ids are configuration defaults, not yet verified with a real OpenRouter key.
- A Hangfire job stopped by a shutdown in the middle of an AI call does not record that last attempt; the job reruns.
