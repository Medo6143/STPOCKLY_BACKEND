# ADR-0001: Stock is a ledger of movements

- Status: Proposed
- Date: 2026-10-12

## Context
Shop owners need a stock number they can trust, and we need to explain every change to it. Invoice confirmation can be retried (network, double tap, offline queue).

## Decision
- `stock_movements` is the source of truth. It is append-only.
- `products.quantity` is a projection, updated only by `IStockService` in the same transaction as the movement. Each movement stores `balance_after`.
- Idempotency is enforced by the database: `UNIQUE (invoice_item_id)` and `UNIQUE (sale_item_id)` on `stock_movements`, plus a `SELECT ... FOR UPDATE` on the invoice and a status check.
- Corrections are new movements (`ManualAdjustment`, `StockCount`, `InvoiceReversal`), never updates or deletes.

## Consequences
- Any quantity can be audited back to its invoice, sale or user.
- Confirm is safe to retry by construction, not by careful coding.
- Slight write amplification (two writes per line). Acceptable at shop scale.
