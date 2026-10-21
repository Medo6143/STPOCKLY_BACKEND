# Orchestration

Moved on 2026-10-21 into Claude Code's own configuration, so the rules are loaded and enforced instead of only read:

| What | Where |
|---|---|
| Coordinator guide (roles, briefs, contract, verification, stop conditions, logs) | `.claude/skills/coordinator/SKILL.md` (run `/coordinator`) |
| Worker and reviewer agents | `.claude/agents/backend-worker.md`, `mobile-worker.md`, `reviewer.md` |
| Git guard for workers | `.claude/hooks/block_git_writes.py` (PreToolUse hook in each worker) |
| Worktrees start from the current branch | `.claude/settings.json` (`worktree.baseRef: "head"`) |

Briefs stay here, in `docs/agents/briefs/`, as the record of each task. The original phase plan (Step 0, Phases 0-2) is in git history: `git log -- docs/agents/ORCHESTRATION.md`.
