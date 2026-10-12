# ADR-0008: Invoice images in S3-compatible object storage (MinIO locally)

- Status: Accepted (2026-10-12)
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

## 2026-10 update: where the local MinIO image comes from

- **Why:** MinIO stopped publishing community container images. `minio/minio` and `minio/mc` no longer resolve on Docker Hub or `quay.io/minio` (checked 2026-10-12: "repository does not exist").
- **Source:** local development uses the community rebuild of MinIO from source published by the Pigsty project: `pgsty/minio` (server) and `pgsty/mc` (client). They are the same MinIO binaries, so `mc ready local`, `mc admin` and the S3 API behave as before.
- **Pinning rule:** both images are pinned by tag **and** sha256 digest in `docker-compose.yml` (`image: pgsty/minio:<tag>@sha256:<digest>`). A bump changes both in the same commit, after checking the new release.
- **Scope:** development and tests only. Production uses a managed S3-compatible store. The app talks to storage only through the AWS S3 SDK behind `IFileStorage`, so the store is a configuration choice (`Storage__Endpoint`, keys, bucket).
- **Fallback:** if the Pigsty images stop being maintained, switch to RustFS (`rustfs/rustfs`, Apache-2.0, S3-compatible). That is a config-only change behind `IFileStorage`: compose image, health check and init script change; application code does not.
- **Sprint 2 note:** Testcontainers' MinIO module defaults to the official `minio/minio` image, which no longer exists. Integration tests must override the module image with the same pinned `pgsty/minio:<tag>@sha256:<digest>` used in `docker-compose.yml`.
