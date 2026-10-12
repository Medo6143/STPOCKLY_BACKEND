# ADR-0011: Polyrepo: stockly-backend (API + Admin) and stockly-mobile

- Status: Accepted (2026-10-12)

## Context
Stockly started as one repository with `backend/`, `mobile/` and `brand/` side by side. The backend and the Flutter app have different toolchains, release cycles, CI and reviewers. The only thing they share at build time is the HTTP contract.

## Decision
- Two repositories:
  - **`stockly-backend`** (this one): the .NET solution at the repo root (`src/`, `tests/`, `Stockly.sln`), `docker-compose.yml`, all product docs (roadmap, ERD, ADRs, design, features, sprint logs). `Stockly.Admin` (Blazor, Sprint 5) stays here because it references `Stockly.Contracts` directly.
  - **`stockly-mobile`**: the Flutter app, `brand/` (logo, splash spec, tokens), the mobile structure doc and the mobile-only rules (former AGENTS.md rules 34–39, Dart conventions, mobile tests).
- **Contract sharing via OpenAPI.** The API publishes an OpenAPI document. It is generated on build and committed at `docs/api/openapi.json`; a test fails if the committed file is out of date (from step 1.4b). `stockly-mobile` generates or hand-writes its Dio client from that file, never from C# types.
- **Each repository has its own CI**: `stockly-backend` runs build, tests, coverage and admin bUnit tests; `stockly-mobile` runs analyze and tests. No cross-repo build dependency.
- Product-wide documents (roadmap, ERD, ADRs) stay in `stockly-backend`; `stockly-mobile` links to them.
- Rule numbers in `AGENTS.md` are kept: 34–39 are reserved with a pointer, so references such as "rule 42" stay valid.

## Consequences
- Backend changes that alter the API show up as a diff in `docs/api/openapi.json`, which is the review point for the mobile team.
- Breaking API changes need coordination across two pull requests; the spec makes them visible early.
- The split happened during Sprint 1, before any mobile code existed, so no mobile history had to be moved. The brand and mobile docs were copied to `stockly-mobile-assets` (the future `stockly-mobile` repository) and removed here; their earlier history remains in this repository's log.
