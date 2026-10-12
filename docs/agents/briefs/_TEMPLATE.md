Read AGENTS.md fully before starting.

# Brief: <lane>-<task>

Repository: `stockly-backend` · Lane: <BACKEND | DOCS> · Contract version: v<N> (`docs/api/contract.md`)

You have not seen the conversation that produced this brief. Everything you need is here or in the files it links. If something you need is missing or contradicts AGENTS.md or the contract, **stop and report** instead of guessing.

## 1. Goal and why
<What to build and why it matters. One paragraph.>

## 2. Scope
**You may change:**
- <exact paths>

**You must NOT touch:**
- Anything not listed above, in particular: <paths>.
- `docs/api/contract.md` (only the orchestrator changes the contract).

**Git and machine safety:** never run git commands that change the working tree or history (`checkout`, `reset`, `stash`, `commit`, `merge`, `push`). To undo your own edit, edit the file back. Never kill processes by image name (`taskkill /IM`, `pkill <name>`, `Stop-Process -Name`); stop only processes you started, by PID. Never change anything outside this repository.

## 3. Critical rules for this task
Full rules: `AGENTS.md`. The ones that matter most here (copied):
- <rule number and text>

## 4. API contract (relevant section)
<Copied from docs/api/contract.md, with its version. Field names, types, status codes and error codes are binding.>

## 5. Steps
1. <Test first: which tests to write>
2. <Implementation>
3. <Docs to update>

## 6. Checks you must pass before finishing
```bash
dotnet build -warnaserror
dotnet test
```
Zero warnings, zero failures. Never skip, delete or weaken a test.

## 7. Do not commit, push or merge.
Leave your changes in the working tree. The orchestrator verifies and commits.

## 8. Final report (exact format)
```
AGENTS.md read: yes
Rules applied: <numbers and one line each>
Files changed: <list>
Check outputs: <tail of dotnet build and dotnet test>
Decisions taken: <list, with reason>
Open questions: <list or "none">
Contract deviations: NONE   (anything else: stop before finishing and report it)
```
