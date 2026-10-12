# ADR-0003: Product units with a base unit

- Status: Proposed
- Date: 2026-10-12

## Context
Pharmacies buy boxes and sell strips. Suppliers write "carton 24". Groceries sell by weight.

## Decision
- Each product has one base unit (`is_base`, factor 1) and any number of other units with a `factor` in base units.
- All quantities in `products` and `stock_movements` are stored in base units as `numeric(14,3)`.
- Invoice and sale lines keep the unit the human used (`product_unit_id`) and the quantity in that unit. Conversion happens in a Domain method `ProductUnit.ToBase(quantity)`.
- Sell price lives on the unit (a box and a strip have different prices). Cost price is per base unit.

## Consequences
- The review screen must let the user pick a unit per line. The AI is asked to return the unit text it sees.
- Conversion rules are pure Domain code with unit tests.
