# ADR-0009: UUIDv7 ids assigned in the Domain

- Status: Accepted (2026-10-12)

## Context
Primary keys are UUIDv7 generated in the app (`docs/erd.md` §1). .NET 8 has no built-in v7 generator, so we use the `UUIDNext` package. AGENTS.md §4 says `Stockly.Domain` references nothing.

Aggregates need their ids before `SaveChanges`. Example: confirming an invoice creates `stock_movements` rows that reference `invoice_item_id` (and new products) inside one transaction. If ids were assigned by EF or an Infrastructure interceptor, factory methods such as `Product.Create(...)` could not return an entity with a usable id, and Domain code could not link new entities to each other.

## Decision
- `Stockly.Domain` may reference the `UUIDNext` package. It is the **only** package Domain may reference. It is pure and has no dependencies.
- The `Entity` base class assigns `Id` as a UUIDv7 at construction. EF never generates ids (`ValueGeneratedNever`).
- Domain still references no project. The allowlist in `tests/Stockly.ArchitectureTests` permits exactly `UUIDNext` for Domain; any other package fails the build.

## Consequences
- Every entity has a stable id from the moment it exists, so one transaction can create and link many rows.
- Time-ordered ids keep B-tree inserts index-friendly.
- Domain has one third-party dependency. On .NET 9+ this can be replaced by `Guid.CreateVersion7()` and the package removed.
