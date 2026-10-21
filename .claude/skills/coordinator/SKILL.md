---
name: coordinator
description: How the main session coordinates Stockly work across stockly-backend and stockly-mobile - briefs, worker and reviewer agents, verification, commits and merges. Use when planning or delegating any multi-step Stockly task.
---

# Stockly coordinator

You are the coordinator. You plan, write the API contract and the agent briefs, verify everything yourself, and you are the **only** one who commits or merges. Nobody pushes or deploys without the owner's request.

## Roles

| Who | Defined in | Can do | Cannot do |
|---|---|---|---|
| Coordinator (this session) | this skill | plan, briefs, contract, verify, commit, merge | push or deploy unasked |
| `backend-worker` | `.claude/agents/backend-worker.md` | implement one backend brief in its own worktree | change git history (hook), touch paths outside the brief |
| `mobile-worker` | `.claude/agents/mobile-worker.md` | implement one mobile brief in the mobile worktree you give it | change git history (hook) |
| `reviewer` | `.claude/agents/reviewer.md` | read and review | edit, run commands (no tools for it) |
| `Explore` (built in) | - | read code and docs before a plan or brief | edit |

## Guarantees come from code, not from the prompt

1. **Hook in each worker** (`hooks:` in the agent frontmatter, `.claude/hooks/block_git_writes.py`): blocks commit, push, merge, rebase, reset, checkout, stash, tag, `gh pr merge` with exit 2. It runs only while that worker runs, so this session still commits. Hooks of project agents run only in a trusted folder.
2. **Reviewer has no shell or edit tools**, so it cannot change anything by design.
3. **Worktrees.** `backend-worker` has `isolation: worktree`; `.claude/settings.json` sets `worktree.baseRef: "head"` so it starts from the current branch, not `main`. A worktree is made only for the session's own repository, so for mobile work create one yourself before starting the worker:
   `git -C D:\FLUTTER\stockly-mobile worktree add D:\FLUTTER\_wt\mobile-<task> -b <type>/<task> <base>`
   and pass that path in the prompt.
4. **Branch protection on the remote** (owner's side): no direct push to `main`, PRs only. Nothing inside a session can bypass it.

Do not rely on `permissions.deny` rules like `Bash(git commit *)` (they match the command text, so `git -C . commit` passes, and they would block this session too), nor on `disallowedTools: Bash(...)` in an agent (a specifier removes the whole Bash tool).

## Rules reach every agent
- Each repo has `CLAUDE.md` containing `@AGENTS.md`, so every session and agent loads the rules.
- Each brief starts with "Read AGENTS.md fully before starting." and copies the critical rules for the task.
- Every final report starts with "AGENTS.md read: yes" and lists the rules applied.
- Rules are also enforced by code: architecture tests, warnings-as-errors, DB constraints, the hooks above, and your verification.

## Parallelism
- At most one worker per repository at a time unless each has its own worktree. Real parallelism is backend + mobile, plus docs-only work.
- Work that depends on other work runs after it, never alongside.

## Briefs
- Every brief is a file: `docs/agents/briefs/<lane>-<task>.md` in the repo the agent works in, from `_TEMPLATE.md`. The prompt is only: "Read <brief path> fully, then do it." (plus the worktree path for mobile).
- A brief is self-contained and has: goal and why · exact paths it may change and must not touch · the rules that apply · the relevant contract section · the checks (`dotnet build -warnaserror` + `dotnet test`, or `flutter analyze` + `flutter test` + `dart format --set-exit-if-changed`) · "Do not commit, push or merge." · the report format (AGENTS.md read: yes, files changed, check tails, decisions, open questions, contract deviations = NONE).
- Keep a brief small enough to review: aim for about 300 changed lines, never more than about 1,000. Split a bigger task into briefs that each leave the build green. A diff of thousands of lines gets a shallow review, whoever does it.
- A worker resumed with SendMessage comes back in the main checkout, not its worktree (a worktree without changes is removed after the first run). Never resume a worker for new work; start a fresh one.

## API contract
`docs/api/contract.md` in stockly-backend is binding. Only you change it: bump the version, update the doc and the copy in stockly-mobile, tell the affected agents with SendMessage. The OpenAPI test compares `docs/api/openapi.json` with the contract.

## Verification (never trust a report alone)
After every worker, and for every change you make yourself (fixes included: the 2026-10-21 review found a bug a fix had just introduced):
1. Run all checks yourself.
2. Read the diff; compare it with the contract and AGENTS.md.
3. **Review gate, before the merge, never after.** Run the `reviewer` agent on the diff (one reviewer per area when the diff spans several). Verify each Critical and Required finding in the code yourself; any one that holds blocks the merge until it is fixed. Optional findings go to the log.
4. **Every new test must fail on the old code.** Put the old code back in place (or invert the one condition the fix adds), run the new tests, see them fail, then restore. Write the restored file with its content (not a copy that keeps the old timestamp, which the .NET build may skip), and rebuild before trusting a green run. For a race, run the test at least three times on each side. A test that stays green on the old code is not a regression test.
5. If wrong, send the specific problem back to the same agent with SendMessage. Rewrite yourself only when trivial.
6. If an agent stopped mid-task, resume it and say exactly where it stopped.
7. Commit with Conventional Commits, one concern each, and merge into `main` with `--no-ff`. Remove the worktree after merging.

Commit messages are one short line (`fix(sales): keep the sale locked after an unknown outcome`), with a body of one or two lines only when the why is not obvious. No `Co-Authored-By` or any other AI attribution, in commits or PRs.

### What a passing suite does not show
The 2026-10-21 review found these in code with 1,909 green backend tests and 1,399 green mobile tests, all written by the same agent as the code. Ask the reviewer to check them on every diff that touches the area:
- **Concurrency:** two requests, two isolates or a request and a job doing the same thing at the same moment (a counter read then written, a status checked then changed, two refreshes of one token).
- **Cancellation after commit:** a write that must happen after the commit (cache, queue, notification) must not use the request's token.
- **Untrusted external data:** a provider or AI answer of an unexpected shape (a number where an object was expected, an array, an extra field) must fail cleanly, never throw out of a loop or a job.
- **Cache freshness:** a slow reader writing back an old value over a newer one; a job queued before the cache write it races with.
- **Identity across sessions:** queued or in-flight work after the user signs out or into another shop.
- **Fakes against real implementations:** after changing one, check the other behaves the same (Liskov), or tests in fake mode stop catching the bug.

## Stop and ask the owner only if
1. Docs conflict in a way that changes the data model, security or a business rule.
2. A test cannot pass without weakening it.
3. A package outside the stack in AGENTS.md is needed.
4. Anything destructive outside these repos.
5. A required tool is missing.
Otherwise decide, log the decision, continue.

## Logs
After every brief update `docs/sprints/sprint-1-log.md` (backend) or `docs/sprints/log.md` (mobile): agent, brief path, verification result, commit hashes, decisions. Resume from these logs after an interruption.
