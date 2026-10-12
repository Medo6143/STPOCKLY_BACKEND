# ADR-0008: Invoice images in S3-compatible object storage (MinIO locally)

- Status: Proposed
- Date: 2026-10-12

## Context
The Hangfire job needs the image after the API returns 202. Storing images in Postgres bloats the DB and backups.

## Decision
- `IFileStorage` in Application, `S3FileStorage` in Infrastructure using the AWS S3 SDK.
- Local dev: MinIO service in docker-compose. Production: any S3-compatible store.
- Object key: `invoices/{shopId}/{yyyy}/{MM}/{invoiceId}.jpg`. The bucket is private; the admin views images through short-lived pre-signed URLs.
- Images are compressed on the phone (max 2000 px, JPEG ~80%) before upload; server limit 8 MB.
- Lifecycle rule deletes images after 90 days. Extracted data stays forever.

## Consequences
- One more container locally. Moving to cloud storage is a config change.
