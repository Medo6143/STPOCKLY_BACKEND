# ADR-0013: Invoice images are kept for 90 days

- Status: Accepted (2026-10-18). Clarifies ADR-0008.

## Decision
- The MinIO bucket has a lifecycle rule that expires invoice images 90 days after upload (`minio-init`).
- A daily Hangfire job (queue `maintenance`, 01:30 UTC) marks `invoices.image_deleted_at` for invoices older than 90 days, so the admin API returns `imageUrl: null` instead of a broken link. It is a system job: no `AdminAuditLog` row.
- Extracted data, raw AI JSON and stock movements are kept; only the photo goes.

## Consequences
- An invoice older than 90 days cannot be re-run (AD19) from its photo.
- The privacy policy states the 90 days.
