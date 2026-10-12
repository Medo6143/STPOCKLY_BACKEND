# ADR-0004: Expiry and batch are informational in v1 (no lot tracking)

- Status: Accepted (decided with product owner, 2026-10-12)

## Context
Pharmacies care about expiry. Full lot tracking (stock per batch, FEFO on sale) adds a table, changes every stock write, and complicates the quick-sale screen.

## Decision
- `invoice_items.expiry_date` and `batch_no` are stored as captured from the invoice.
- Stock is tracked per product, not per lot.
- The dashboard shows an "expiring soon" card from `invoice_items` where `expiry_date` is within 60 days and the product still has stock.

## Consequences
- Simple MVP. The card is a hint, not an exact count of expiring units.
- Moving to lot tracking later means adding `stock_lots` and backfilling from `invoice_items`. The data needed for that backfill is kept from day one.
