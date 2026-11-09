# Complex delegation in one shared working tree

Read before starting subagents or deciding parallel execution for `complex` plans.

## Shared workspace contract

- All agents work in **the same repository and working directory**. They do not get isolated Git worktrees, individual branches or private copies as part of this workflow.
- Separate agent context **does not imply filesystem isolation**. File ownership is a coordination convention, not a filesystem lock or security boundary.
- Before dispatch, inspect relevant changed files (including user changes), dependency outputs and likely touchpoints. Map each active agent to a **non-overlapping edit boundary** (paths/subsystems plus any shared contracts). Main agent coordinates common files.
- If ownership is uncertain or edits overlap, serialize those packages or ask a running subagent to pause/hand back the shared change. Never rely on a simultaneous, uncoordinated merge of edits in one file.
- Avoid concurrent Git operations that mutate shared repository state (checkout, rebase, cherry-pick, reset, stash, commits, etc.). Do not perform destructive operations on unrelated user work. Shared solution/project files and plan documents are main-agent-owned unless a strictly non-overlapping modification is explicitly assigned.
- Avoid parallel builds/tests that mutate the same outputs, caches, databases, ports or generated code. Use serialized runs when safe isolation of those artifacts is not established.

## Dynamic scheduler

1. Read the latest approved plan. A package is *ready* when all `depends_on` packages are accepted (`completed`), not merely when their agents say "done".
2. Choose a small number of ready packages with meaningfully independent edit boundaries and bounded subagent cost. If dependency or collision risk is high, execute sequentially.
3. Before dispatch, specify each package's ownership, permitted changes, forbidden shared changes, dependencies and required checks. Reserve common contract changes for the main agent or a single sequential package.
4. Update the main plan status to `in_progress` for dispatched packages; only the main agent writes the plan. A dispatch does not automatically mean acceptance.
5. When results return, inspect actual repository diffs and test evidence. Resolve any unanticipated shared edits before accepting another package. Re-check readiness and repeat.
6. If the environment cannot spawn subagents, **do not pretend delegation or independent review happened**. Record the capability blocker; follow explicit user direction before switching to a different execution model.

## Minimum subagent assignment

Send just-in-time context rather than prewriting long prompts during planning:

```text
Work package: WP-02 — <goal>
Source design: <path>, revision <n>; relevant decisions D1/D3 and constraints: <excerpts>
Plan: <path>, revision <n>; relevant acceptance: AC2, AC3
Dependencies: WP-01 accepted; relevant outputs: <short summary / paths>
Development mode: strict-tdd
Edit ownership: <specific files/subsystems>; do not edit plan, design or shared contracts
Checks: <package-specific tests or validation>
If you need an out-of-bound change, stop that edit and report the proposed change.
Report: files changed, behavior, tests run + outputs, assumptions, unresolved risks.
```

- Never send the entire brainstorm history or complete project plan if relevant excerpts suffice. Agent may read specific referenced local files when necessary.
- Subagent should inspect its *actual changed files* before returning, avoid touching unrelated changes, and state which checks were run. It must not self-approve the package or autonomously change architecture.
- Main agent validates returned evidence, including changes shared with other agents and claims about tests. A subagent's summary alone does not prove completion.

## Integration

- Package boundaries need not align exactly with final file ownership; the main agent reconciles shared interfaces, generated artifacts and integration points once isolated packages finish.
- The actual shared repository already contains edits; do not blindly `cherry-pick` or copy a subagent patch onto those same edits.
- Coordinate a clean build/test sequence and verify end-to-end AC coverage after integration. Record blockers and failed checks rather than silently marking them passed.
