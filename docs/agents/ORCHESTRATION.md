New way of working for the rest of the project. You are the ORCHESTRATOR. Save this whole message as docs/agents/ORCHESTRATION.md in stockly-backend (commit it as "docs: add orchestration guide") and follow it from now on.

## Current state
- Merged into main: 1.4a persistence, repo split (ADR-0011), .NET 10 upgrade (ADR-0010).
- D:\FLUTTER\stockly-mobile-assets holds brand/, AGENTS.mobile.md and docs/structure.mobile.md.
- 1.4b (API cross-cutting) is committed on feature/api-cross-cutting, NOT merged. The full branch builds with 0 warnings and 276 tests pass, but the per-commit build check fails on all 4 commits.

## Step 0: finish 1.4b (time-box: 20 minutes)
1. Build the FIRST commit in a fresh worktree OUTSIDE the repo folder (e.g. D:\FLUTTER\_wt\c1): dotnet restore && dotnet build -warnaserror. Paste the first 20 error lines into the log.
2. Check these causes in order:
   a. The build needs git-ignored files (.env, user-secrets). A build must NEVER need secrets. The likely culprit is OpenAPI generation at build time starting the host, with ValidateOnStart rejecting the missing config. Fix it: generate docs/api/openapi.json from a test (WebApplicationFactory with test config), not during dotnet build. Prove it: a clean clone with no .env builds and tests green.
   b. Commit order: a commit uses a package or file that only arrives in a later commit. Fix with git reset --soft main and recommit in buildable groups (the branch isn't pushed).
   c. Worktree placement or stale obj/bin only: the code is fine. Log it and skip the per-commit check for this branch.
3. If there's no root cause after 20 minutes: squash into ONE commit "feat: add api cross-cutting concerns", confirm build + tests green from a clean clone without .env, merge, and log the open question.
4. Merge into main with --no-ff. Write the root cause in one line in the log.

## Roles
- You: plan, write the API contract, write agent briefs, verify everything yourself, commit, merge. You are the only one who commits or merges. Nobody pushes or deploys.
- Explore agents (read-only): use them to read code and docs before writing a plan or a brief.
- Worker agents (Agent tool, background, parallel): implement one brief each. They NEVER commit, push, merge, or touch files outside their brief.

## Rules reach every agent automatically
- stockly-backend: add CLAUDE.md at the repo root containing only the line: @AGENTS.md
  (an import, so Claude Code loads AGENTS.md in every session and subagent).
- stockly-mobile: same CLAUDE.md, where AGENTS.md comes from AGENTS.mobile.md.
- Add docs/agents/briefs/_TEMPLATE.md in each repo. Its first line is "Read AGENTS.md fully before starting." It has a "Critical rules for this task" section where you copy the rules relevant to that task.
- Every final report starts with "AGENTS.md read: yes" and lists the rules the agent applied.
- Rules are also enforced by code, not only by instructions: architecture tests, warnings-as-errors, DB constraints, and your own verification.
Commit as "docs: load agent rules automatically and add brief template".

## Parallelism rules
- At most ONE worker per repository at a time. Two agents running dotnet build (or flutter build) in the same repo collide. Real parallelism is stockly-backend + stockly-mobile, plus docs-only work.
- Work that depends on other work runs after it, never alongside.

## Briefs
- Every brief is a FILE: docs/agents/briefs/<lane>-<task>.md in the repo the agent works in. The agent prompt is only: "Read <brief path> fully, then do it." This prevents truncated instructions.
- A brief is self-contained (the agent has not seen our conversation) and contains:
  1. Goal and why.
  2. Exact paths it may change, and paths it must NOT touch.
  3. The rules that apply (link AGENTS.md and copy the critical ones).
  4. The relevant section of the API contract.
  5. The checks it must pass before finishing: dotnet build -warnaserror + dotnet test (backend), or flutter analyze + flutter test + dart format --set-exit-if-changed (mobile).
  6. "Do not commit, push or merge."
  7. Its final report format: "AGENTS.md read: yes", files changed, check outputs (tails), decisions taken, open questions, contract deviations (must be NONE, or stop and report).

## API contract (written by you, BEFORE any lane starts)
Write docs/api/contract.md in stockly-backend, version v1. Cover:
- Every endpoint for Sprints 1–4: auth, products and units, adjust/stock-count, invoices scan/get/items/confirm, sales, dashboard, low stock, expiring soon. For each: method, path, auth/role, request and response JSON with field names and types, and status codes.
- Error format: RFC 7807 ProblemDetails with a "code" extension using our Error codes (ProductErrors, StockErrors...), plus the full list of codes per endpoint.
- Conventions:
  - camelCase JSON.
  - Money and quantities as decimal STRINGS ("125.50", "1.500"), because Dart double breaks money. Mobile parses them with the decimal package.
  - Ids are UUID strings. Timestamps are ISO-8601 UTC. Dates are yyyy-MM-dd.
  - Paging: { items, page, pageSize, totalCount }, pageSize max 100.
  - Idempotency: clientRequestId (UUID) on scan and sale.
  - Auth: access token 15 min + refresh token with rotation; on 401 the client refreshes once and retries once.
  - Localization: Accept-Language ar|en for user-facing messages.
Once lanes start, the contract changes only through you: bump the version, update the doc, update the copy in stockly-mobile, and message the affected agents with SendMessage. Add a backend test that compares docs/api/openapi.json (generated in a test) with contract.md for paths, methods and status codes.

## Verification (never trust a report alone)
After every worker finishes:
- Run all checks yourself.
- Read the diff.
- Compare it with the contract and AGENTS.md.
- If wrong, send the specific problem back with SendMessage to the same agent. Do not rewrite it yourself unless it's trivial.
- If an agent stopped mid-task (session closed), resume it with the same tool and tell it exactly where it stopped.
Then commit in Conventional Commits and merge into main with --no-ff.

## Stop conditions (stop and ask me only if)
1. The docs conflict in a way that changes the data model, security or a business rule.
2. A test cannot pass without weakening it.
3. A package outside the stack in AGENTS.md is needed.
4. Anything destructive outside these repos.
5. A required tool is missing (for example the Flutter SDK).
Otherwise decide, log the decision, and continue.

## Plan from now
Phase 0 (you, sequential): Step 0 (finish 1.4b) → CLAUDE.md + brief template → write docs/api/contract.md v1. Report to me (checkpoint 1).

Phase 1 (parallel, after I approve the contract):
- Lane BACKEND (one worker at a time, in stockly-backend): 1.5 auth → 1.6 products API, one brief per step, each verified and merged before the next brief.
- Lane MOBILE (in a NEW repo stockly-mobile, created by you from D:\FLUTTER\stockly-mobile-assets: git init -b main, AGENTS.md from AGENTS.mobile.md, CLAUDE.md, brand/, .gitignore, .editorconfig, docs/structure.md, a copy of contract.md in docs/api/, the brief template):
  - First check that the Flutter SDK is installed (flutter --version). If not, stop and tell me.
  - Brief 1: write docs/design/frontend.md: layers per feature, Riverpod provider graph, go_router routes + auth redirect, Dio interceptors (auth, refresh with request queue, ProblemDetails → Failure), offline scan queue (Drift schema + sync), theme tokens from brand/ (light/dark ThemeExtension), l10n (ar default, en), AsyncStateView, splash from brand/stockly-brand.html. STOP after this and report to me (checkpoint 2).
  - Brief 2+ (after I approve frontend.md): Flutter foundation, splash, auth screens, product screens, built against the contract with fake repositories (no live backend needed). Widget tests for 4 states × 2 languages.
- Lane DOCS (docs-only worker, in stockly-backend, can run anytime): docs/design/backend.md for Sprint 2: scan/poll/confirm sequence diagrams, OpenRouter DTOs, full system prompt, Polly policy, IInvoiceValidationRule list.

Phase 2 (you): 1.7 sprint review for the backend from a clean clone (docker compose up -d, dotnet ef database update, run the API, register → login → product → adjust → low stock via a committed .http file). Then connect the mobile app to the real backend (switch the fakes to the real Dio data sources) and run an end-to-end test: register → login → product → adjust, in both languages. Report to me (checkpoint 3).

## Logs
Keep docs/sprints/sprint-1-log.md (backend) and docs/sprints/log.md (mobile) updated after every brief: which agent, brief path, verification result, commit hashes, decisions taken. If the session is interrupted, resume from these logs.

## Report to me at
(1) contract v1 ready, (2) frontend.md ready, (3) end of Phase 2. At each checkpoint: what was done, test counts, decisions taken, risks, and what's next.
