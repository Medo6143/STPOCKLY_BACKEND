# ADR-0010: UUIDv7 via Guid.CreateVersion7

- Status: Accepted (2026-10-12)
- Supersedes: ADR-0009

## Context
ADR-0009 let `Stockly.Domain` reference the `UUIDNext` package because .NET 8 had no built-in UUIDv7 generator. The project moved to .NET 10 LTS (.NET 8 support ends 2026-11-10). Since .NET 9 the BCL has `Guid.CreateVersion7()`.

## Decision
- `Entity` assigns `Id = Guid.CreateVersion7()` at construction. The rest of ADR-0009 stays: ids exist before `SaveChanges`, EF never generates them (`ValueGeneratedNever`).
- `Stockly.Domain` references **no packages**. The architecture tests allow none for Domain (`ProjectReferenceTests`, `LayerDependencyTests`).
- UUIDNext is removed from `Directory.Packages.props`.

## Ordering guarantee (what changed)
- `Guid.CreateVersion7()` puts the Unix time in milliseconds in the first 48 bits, so ids from **different milliseconds** sort in creation order, as bytes, as strings and as PostgreSQL `uuid`. B-tree inserts stay index-friendly.
- Inside **one millisecond** the remaining bits are random: there is no counter, unlike UUIDNext. Measured: 100 ids created in a tight loop were out of order in 1000 of 1000 runs.
- Therefore: never use `Id` to order events that can happen in the same millisecond. Use a timestamp column (`created_at`) for ordering, as every list query already does.
- The id tests assert this guarantee: version 7, RFC 9562 variant, embedded timestamps never decrease, and ids from different milliseconds sort in creation order.

## Consequences
- One less third-party dependency, and none in the Domain.
- Existing ids stay valid: both generators produce RFC 9562 UUIDv7 values.
