# Invoice image lifecycle

Sprint 6. Invoice photos are kept **90 days** (`InvoiceLimits.ImageRetentionDays`), long enough to review, confirm and
debug a scan, short enough to keep storage and personal data small. Extracted data (`invoices`, `invoice_items`,
`raw_ai_json`) is kept; only the photo goes.

## 1. Storage side: MinIO lifecycle rule

`minio-init` (`docker-compose.yml`) adds one bucket lifecycle rule to the invoice bucket:

```sh
mc ilm rule add --expire-days 90 "local/$MINIO_BUCKET"
```

MinIO deletes every object 90 days after it was written. The step is idempotent: it lists the rules as JSON first and
skips the add when a 90-day expiry exists. Check it with `mc ilm rule ls local/<bucket>`. The rule also removes an
orphan image left by a scan whose insert failed (`ScanInvoiceCommandHandler` uploads before it inserts).

For an existing local stack, re-run the one-shot service: `docker compose up minio-init`. On a managed S3-compatible
store, configure the same rule in its console or with its CLI (expire current objects after 90 days; the bucket is not
versioned).

## 2. Database side: `invoices.image_deleted_at`

MinIO does not tell the Api when it expires an object, so a daily job records it:

- `ExpireInvoiceImagesJob` (`src/Stockly.Infrastructure/Jobs`), Hangfire recurring job `invoice-image-expiry`, cron
  `30 1 * * *` UTC (03:30 Cairo in winter), queue `maintenance` (served after `invoices` by the in-process server). It is
  registered (add-or-update) each time the server starts.
- It sends `ExpireInvoiceImagesCommand` (`Application/Features/Admin/ExpireInvoiceImages`), which calls
  `IAdminInvoiceImageRetention.MarkImagesDeletedAsync(createdBefore: now − 90 days, now)`: one
  `UPDATE invoices SET image_deleted_at = @now WHERE image_deleted_at IS NULL AND created_at < @cutoff` across every
  shop (cross-tenant, so it lives in `Infrastructure/Persistence/Admin/`, ADR-0005), served by the partial index
  `ix_invoices_created_at_image_kept` (`docs/erd.md` §3.3). Idempotent; a marked invoice keeps its first date.
- A system job, not an admin action: no admin user, no `admin_audit_logs` entry. It logs only the number of rows.

The cut-off uses the invoice's `created_at`, which is the upload time: the image is uploaded in the same request, just
before the insert.

## 3. Effect on the API

- **AD18** (`GET /api/admin/invoices/{id}`) answers `imageUrl: null` once `image_deleted_at` is set, without asking
  storage (contract: "null when the object is gone"). Before the job runs, a missing object also gives `null` (the
  pre-signed URL is only issued after a `HEAD`).
- **AD19** (re-run of a failed invoice) on an invoice whose image is gone fails again with `ImageMissing`, as before.
- The shop endpoints never return the image, so nothing changes for the mobile app.

## 4. Tests

`ImageRetentionPersistenceTests` (PostgreSQL: old images of every shop marked in one statement, exactly 90 days kept,
already marked untouched, second run marks nothing), `AdminInvoiceAndMiscHandlerTests` (cut-off = now − 90 days; AD18
with a marked image returns `null` without calling storage), `ImageRetentionEndpointTests` (scan, age the invoice, run
the command, AD18 `imageUrl: null` while a recent invoice is still signed), `MigrationTests` (column and partial index).
