# ADR-0012: Latin key for cross-script product matching

- Status: Accepted (2026-10-18)

## Context
Supplier invoices mix Arabic and Latin spellings of the same product ("بنادول اكسترا" / "Panadol Extra"). Trigram similarity only compares like with like, so a cross-script pair never matches.

## Decision
- Every product and alias stores `latin_key`: a deterministic consonant skeleton of the normalized name (Arabic letters transliterated with Egyptian readings, vowels dropped), computed in the Domain (`LatinKey`) on write.
- The migration backfilled existing rows with an SQL function `stockly_latin_key`, kept for re-backfills; a parity test keeps it identical to the C# version.
- `TrigramMatchStrategy` (order 30) compares both the normalized name and the latin key and takes the higher similarity. Its confidence is capped at the exact strategy of the same kind, so a fuzzy match never looks surer than an exact one.

## Consequences
- Changing the transliteration means a migration that re-backfills `latin_key` with the new SQL function.
- Short names can collide on the skeleton; the confidence caps and the review screen's "check this match" hint keep the user in control.
