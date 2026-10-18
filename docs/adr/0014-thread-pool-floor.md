# ADR-0014: Thread-pool minimum of 64 for the API

- Status: Accepted (2026-10-18)

## Context
The load test showed that a burst of scans after an idle period starved the thread pool: Hangfire's enqueue is synchronous, and I1 p95 reached 22.6 s for a 120-scan burst.

## Decision
`ThreadPoolMinThreads=64` in `Stockly.Api.csproj`. The same burst then had p95 0.86 s.

## Consequences
Revisit when Hangfire offers an async enqueue or when the API runs on a host with a different core count; the load test in `docs/operations/load-test.md` is the check.
