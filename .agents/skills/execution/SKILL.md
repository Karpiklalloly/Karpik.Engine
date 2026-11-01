---
name: execution
description: Execute an explicitly approved medium or complex development plan saved by the planning skill. Use for implementing or resuming approved structured tasks, coordinating complex work packages with subagents in a shared working tree, integrating changes, verifying acceptance criteria, and arranging independent review. Not for brainstorming or drafting/approving plans.
---

# Execution

Execute **the approved plan**, preserving its design, user constraints, acceptance criteria (ACs), and development mode. Medium work stays in the current agent; complex work is delegated in cohesive work packages when subagent support exists. **Only the main agent accepts results and edits the shared plan.** Do not ask for another approval just to start execution or use subagents.

## Entry contract

- Input: exact `plan_path` (or an unambiguous path to recover), saved source design, `complexity`, `development_mode`, `plan_revision`, `design_revision` and approval fields. Read only relevant sections of those files and applicable project instructions.
- Execute only `approved` / `executing` plans with recorded `approved_plan_revision` and `approved_design_revision` from **explicit user consent**. `awaiting_approval`, `draft`, `blocked`, or `outdated` is not execution permission; return to the responsible workflow/skill. No fabricated approval.
- Reconcile current design revisions and effective plan changes with the approved baseline. Post-approval technical adjustments are allowed only if behavior, architecture, scope, constraints and ACs remain unchanged. Material changes require affected-scope brainstorming, replanning and renewed approval before dependent work proceeds.
- If the user explicitly requests *only* inspection or planning, respect that scope even if approval metadata exists. Respect host/tool permission requirements separately from workflow approval.

## Procedure

1. **Recover and inspect.** Read [references/state-and-recovery.md](references/state-and-recovery.md) when starting/resuming, changing plan state or hitting a blocker. Check plan/design identity and revision, approval, repository status and existing changes. Preserve unrelated work; never assume a marked-completed item is actually present without evidence.
2. **Select applicable mode.** Use the chosen `normal`, `tdd`, `strict-tdd` or `prototype` rules (load a development-mode skill if available). In strict TDD, apply demonstrable Red → Green → Refactor to applicable behavior changes; do not silently downgrade because a test is inconvenient. Treat tool outputs as evidence, not as proof of actions not actually performed.
3. **Start execution.** Main agent records `status: executing` in the **same plan** and selects ready items whose dependencies are accepted. Do not recreate a plan or request reapproval for harmless technical scheduling changes.
4. **Medium: implement directly.** Complete structured `T...` tasks in the current chat, in dependency order. Use task-specific checks where needed and global/integration verification at the end. Do not spawn implementation or review subagents merely for Medium.
5. **Complex: coordinate work packages.** Read [references/shared-workspace.md](references/shared-workspace.md) before delegation. Delegate cohesive `WP-...` packages to subagents with only relevant design excerpts, constraints, dependency results, AC IDs and verification expectations. All agents share **one working directory**: no worktrees or branch-per-agent scheme. Main agent decides readiness/concurrency dynamically; no preassigned waves or agents.
6. **Accept and integrate.** Main agent validates every reported package against actual code changes, scope, acceptance and test evidence; records `completed` only when accepted. Integrate cross-package contracts and verify global/end-to-end ACs. Any shared build/test output, overlapping edits or mutating Git operations must be coordinated, not run concurrently unsafely.
7. **Independent review for Complex.** After integration and global verification, assign a **separate subagent not involved in implementation** a review-only task. Have it inspect the actual changes against design, plan, ACs, constraints and tests; it must not edit files. Triage findings, correct genuine defects and recheck. See [references/review-and-retries.md](references/review-and-retries.md).
8. **Finish honestly.** Mark the plan `completed` only when all ACs, required checks and (for Complex) independent review pass with no accepted blocking findings. Otherwise persist `executing` or `blocked`, record exact outstanding checks/issues, and report partial progress instead of claiming success.

## Boundaries and escalation

- Main agent owns plan metadata, statuses, task split/merge, dependency changes, shared contracts, integration and final acceptance. Subagents implement within assigned boundaries and **report** outcomes; they never mark packages accepted or edit the shared plan/design.
- Execution may reorder/split/merge packages, add technical work and checks or refine private implementation, and note *meaningful* adjustments in the plan. Increment `plan_revision` for material decomposition/dependency/verification changes, **not** ordinary progress updates. Preserve approval for design-compatible changes.
- New requirement, architecturally significant public API/contract change, incompatible AC, or violated design constraint -> stop affected work; return to `brainstorming` and `planning` for affected-scope decisions/approval. Preserve unrelated completed work.
- Missing test infrastructure, permission, unavailable subagent facility or repeated failure is not a reason to invent results or weaken strict TDD. Escalate only the real blocker and persist recovery information. For Complex, do not claim independent review happened if no independent reviewer is available.
- Do not reset, clean, stash, commit, force-push or delete unrelated user changes without explicit instruction. Never assume isolation exists merely because agents have separate conversations.

## Efficiency and handoff

- Read files and references on demand; avoid reproducing design or full plan in agent prompts. Prefer a few cohesive packages to many tiny subagent tasks. Do not delegate inherently sequential work for its own sake.
- Save checkpoints at meaningful boundaries (task/package acceptance, significant plan adaptation, blocker, review completion). Avoid logging every edit or shell command.
- Final report: completed scope, actual verification and review results, unresolved issues, saved plan path/state, and any material deviations. Distinguish **passed**, **failed** and **not run** checks.
