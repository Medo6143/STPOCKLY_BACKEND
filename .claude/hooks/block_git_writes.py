"""PreToolUse hook for worker subagents: refuse git commands that change history or the working tree.

Workers leave their changes uncommitted; only the coordinator (the main session) commits and merges.
The rule matches `git` anywhere in the command, so `git -C . commit` and `bash -c "git push"` are caught.
Exit code 2 blocks the call and sends stderr back to the worker.
"""
import json
import re
import sys

BLOCKED = re.compile(
    r"\bgit\b.*\b(commit|push|merge|rebase|reset|cherry-pick|revert|am|tag|checkout|switch|restore|stash|update-ref)\b"
    r"|\bgit\b.*\bbranch\b.*\s-[dDmMfF]\b"
    r"|\bgh\s+pr\s+merge\b",
    re.IGNORECASE | re.DOTALL,
)

MESSAGE = (
    "Blocked: workers must not change git history or the working tree "
    "(commit, push, merge, reset, checkout, stash...). Leave your changes uncommitted; "
    "the coordinator reviews, commits and merges. To undo your own edit, edit the file back."
)


def main() -> int:
    try:
        payload = json.load(sys.stdin)
    except json.JSONDecodeError:
        print("Blocked: the git guard could not read the hook input.", file=sys.stderr)
        return 2
    command = (payload.get("tool_input") or {}).get("command") or ""
    if BLOCKED.search(command):
        print(MESSAGE, file=sys.stderr)
        return 2
    return 0


if __name__ == "__main__":
    sys.exit(main())
