---
name: planning
description: Turn a completed, saved brainstorming design into a persisted implementation plan for medium (structured tasks) or complex (independent work packages) development. Use when the development workflow or user requests planning after design. Do not use for brainstorming or implementation.
---

# Planning

Produce **one saved, executable plan with one approval gate**. Do not implement, run subagents, request separate design approval, or request approval again merely to schedule execution.

## Contract

- Input: exact brainstorming document path/revision, complexity and whether it was auto/manual, development mode, and relevant project context (derive missing nonessential settings; never invent decisions).
- Require a **saved** brainstorm with `Status: ready`, `Revision`, stable acceptance IDs (`AC...`) and no open in-scope questions. Otherwise hand back to `brainstorming`; `ready` does **not** mean implementation is approved.
- Brainstorming owns requirements, behavior, architecture, significant public contracts, constraints, decisions (`D...`) and ACs. Planning owns tasks, internal implementation choices, dependencies, verification and plan presentation. Never silently revise architectural decisions.
- Workflow owns skill routing and user approval; execution owns scheduling, delegation, integration, status updates and runtime adaptations. Planning must not prescribe agent assignments or parallel waves.

## Steps

1. **Resume or start.** Reuse the plan for this design if one exists. Read [references/approval-and-state.md](references/approval-and-state.md) for resumption, states, revisions and approval rules; avoid duplicate files.
2. **Validate and investigate.** Read the saved brainstorm and its current revision; inspect only relevant code, tests, dependencies and conventions. Don't copy the architecture into the plan or revisit resolved decisions without new evidence.
3. **Recheck complexity.** If initially `auto`, change `medium` ↔ `complex` when uncertainty, integration risk or subsystem boundaries justify it; report the adjustment. If it is actually `light`, return to workflow's light path. If manually chosen, preserve the level, raising concrete feasibility/safety concerns rather than silently overriding it. File count alone is not a complexity measure.
4. **Build plan.** Read [references/plan-formats.md](references/plan-formats.md). For `medium`, use short structured tasks (`T1`, ...) with goal and mapped ACs; optional dependencies/targeted checks. Main agent executes in the current chat. For `complex`, use self-contained work packages (`WP-01`, ...) with goal, scope/boundaries, explicit dependencies, AC mapping and package checks. Execution chooses concurrency dynamically.
5. **Respect development mode.** Preserve manual `normal`, `tdd`, `strict-tdd` or `prototype`; resolve `auto` with workflow context before approval. Reflect test-first steps for TDD, verifiable Red → Green → Refactor for suitable behavior changes in strict TDD, or minimum hypothesis checks for prototypes. Don't duplicate the full testing-mode instructions.
6. **Plan verification.** Always include global/end-to-end AC and integration verification. Medium adds task-specific checks only when useful for distinct risks. Complex adds checks for **every package** and an **independent review subagent after integration**. Avoid repeating identical tests in each task.
7. **Self-check.** Cover every AC in an item or explicit integration check; ensure goals are verifiable, dependencies exist and have no cycles, packages have workable boundaries, integration/conflicts are considered, and constraints/development mode are preserved. Resolve ordinary technical problems yourself.
8. **Save and request one approval.** Persist **one separate Markdown plan for either Medium or Complex** (start at `draft`, then `awaiting_approval` after validation). Verify the write. Present a concise summary of tasks/packages, dependencies, risks and checks; request **one approval of the final plan**, not design and plan separately. User revisions remain within the same approval process.
9. **Handoff after explicit approval.** Record `approved_plan_revision`, `approved_design_revision`, and `status: approved` only after clear consent. Pass the real plan path, revisions, complexity, and development mode to workflow/execution. Never start implementation while acting as planning.

## Exceptions and persistence

- When a **new architecture/behavior question** or design contradiction appears, reopen only affected issues in the **same** brainstorming document. Resume planning after it returns to `ready`; update only impacted tasks. A revised plan requires approval only where agreed scope/behavior changed, not a separate design gate.
- If external information/access prevents progress, persist `blocked` with the needed action. An ordinary pause is `draft` or `awaiting_approval`, **not** `blocked`. Do not silently downgrade an infeasible development mode.
- After approval, execution may split/merge/reorder/add technical work without new consent if architecture, constraints, scope and ACs stay intact. Only the **main agent** updates the shared plan; subagents return evidence, not accepted statuses. See [references/approval-and-state.md](references/approval-and-state.md).
- On resume, compare the plan's design revision to the actual design and verify relevant repository/test state before trusting completion marks. Do not repeat finished work without reason.
- If writing fails, show the full plan in chat, disclose failure and **do not claim durable persistence or readiness for execution**.

Keep the plan and prompts compact. Don't create per-package plans, prewrite subagent prompts, spawn review agents merely to validate planning, or report unobserved tests/approval as completed.
