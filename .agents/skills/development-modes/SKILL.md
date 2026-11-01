---
name: development-modes
description: Select and apply normal, TDD, strict TDD, or prototype development rules for code changes. Use when a development workflow, plan, or user specifies a development mode, or asks for automatic test-first mode selection.
---

# Development modes

Define **how** code is implemented and verified. Complexity (`light`, `medium`, `complex`) determines the workflow; development mode determines coding/test discipline. These axes are independent. Do not run brainstorming, approve plans, or schedule subagents from this skill.

## Entry and routing

1. Obtain `requested_mode`: `auto`, `normal`, `tdd`, `strict-tdd`, or `prototype` from the user/workflow/plan. Explicit user selection overrides project defaults and automatic recommendations.
2. If `auto`, read [references/auto-selection.md](references/auto-selection.md), choose the **effective mode** for each relevant change, and retain the fact that selection was automatic. `auto` never independently chooses `strict-tdd` or `prototype`.
3. Read **only** the selected mode's reference: [normal](references/normal.md), [tdd](references/tdd.md), [strict-tdd](references/strict-tdd.md), or [prototype](references/prototype.md). When either TDD mode lacks usable infrastructure, also read [test-infrastructure](references/test-infrastructure.md).
4. Before editing, inspect the affected code, available tests and test/build commands. Use project conventions and the selected mode without inventing new architecture or silently widening scope.
5. Implement with that mode's sequence; run applicable checks and record factual results. Explain skipped or blocked checks explicitly; never claim a test was run or a Red phase observed without evidence.

## Manual selection and overrides

- A **manually chosen** mode is binding throughout the requested scope. Do not silently downgrade TDD, strict TDD, or prototype to another mode for convenience, missing dependencies, or time pressure. Any exception to a manual mode requires explicit user agreement.
- In `auto`, select a mode per task/work package when helpful. A resolved plan-level default is stored as `development_mode`; record per-item deviations **only when necessary**. A per-task override can be explicitly chosen by the user; do not use auto-generated overrides to weaken a manual selection.
- If a plan already records a resolved effective mode, respect it. For a later, valid `auto` reselection, update the relevant plan item via the main agent; do not rewrite approved architecture or silently change user instructions.
- For `complex`, the main agent passes a package's effective mode to its subagent. Subagents apply the same rules and report test evidence; they do **not** edit the shared plan or choose a weaker mode.
- For `light`, apply the mode directly, even when no separate brainstorming/plan document exists. Medium stays with the main agent; Complex execution/delegation remains owned by `execution`.

## Test infrastructure is mandatory for TDD

- **If `tdd` or `strict-tdd` is selected and suitable test infrastructure does not exist, establish it before beginning behavior implementation.** This also applies when `auto` selects `tdd`.
- Do not automatically fall back to `normal` because setting up tests seems expensive. Infrastructure setup is necessary technical work; integrate it into the current task/plan while honoring existing approvals and architecture constraints.
- If setup cannot be completed (permissions, missing dependencies/tools, unavailable test environment, or an architectural change requiring approval), record the concrete blocker and request only the decision/access needed. Do not pretend TDD was performed.
- Read [references/test-infrastructure.md](references/test-infrastructure.md) for setup, verification and scope rules.

## Quality and completion contract

- All modes require verification proportional to their intended outcome and project risks. `prototype` reduces breadth, **not honesty or safety**; `normal` is not "no tests."
- `tdd` prefers test-first for testable behavior and documents justified exceptions. `strict-tdd` requires evidenced Red → Green → Refactor for every applicable behavior change; unrelated infrastructure/doc steps receive appropriate direct checks.
- Never rewrite requirements, public contracts, acceptance criteria or design constraints merely to make a test pass. Route architecturally consequential changes back through the owning workflow.
- Preserve unrelated user edits. Never run destructive Git actions as part of mode selection or testing.
- Report concise evidence: effective mode, tests/checks executed with pass/fail/not-run, any test infrastructure created, mode exceptions (if approved), and unresolved blockers. Reuse the saved plan's verification/state sections rather than creating redundant logs.
