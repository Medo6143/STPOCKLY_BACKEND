# ADR-0006: Offline scan queue with client request IDs

- Status: Accepted (2026-10-12)
- Date: 2026-10-12

## Context
Shops often have weak internet. The owner must be able to photograph invoices offline, and uploads may be retried many times.

## Decision
- Mobile stores pending scans (compressed image file + metadata) in a local Drift (SQLite) queue.
- Each scan gets a `client_request_id` (UUID) when the photo is taken. The same ID is sent on every retry.
- Backend: `UNIQUE (shop_id, client_request_id)` on `invoices`. A retry returns the existing invoice (200 with its current status) instead of creating a new one.
- The same pattern applies to quick sales (`sales.client_request_id`).
- The queue uploads in the background when connectivity returns (`connectivity_plus` + `workmanager`).

## Consequences
- Retries are free and safe.
- Daily quota is counted at upload time, not at capture time. The app shows queued scans as "waiting for internet".
