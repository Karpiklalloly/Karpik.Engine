---
name: development-workflow
description: Coordinate code changes and resumed development through automatic or user-selected light, medium, or complex workflows, development modes, one plan approval, and optional subagents. Route to brainstorming, planning, development-modes, and execution without unnecessary questions or token use.
---

# Development workflow

Route development through the **smallest adequate process**. Two independent settings:

- `complexity`: `auto` (default), `light`, `medium`, `complex`.
- `development_mode`: `auto` (default), `normal`, `tdd`, `strict-tdd`, `prototype`.

Current user instructions override project defaults, which override these defaults. Explicit manual choices are binding; never silently downgrade or override them.

## Entry

1. Determine whether the user wants **implementation**, **design only**, **planning only**, **review/analysis only**, or **resumption**. Do not implement when only discussion, planning or review was requested.
2. If resuming, locate the existing saved design/plan and follow its real status; do not start over or reapprove an applicable approved plan. Read [references/routing-and-approval.md](references/routing-and-approval.md) for routing, resumption and approval details.
3. If complexity is `auto`, select using risk, requirements uncertainty, architectural consequences and integration effort—not file count. `light` is clear/local/low-risk; `medium` needs bounded architectural discussion; `complex` warrants significant architecture/integration and autonomous work packages. Consult [references/classification.md](references/classification.md) for ambiguity or reclassification. Planning may adjust an **auto** classification; respect manual selections.
4. Load relevant sibling skills **only at the phase that needs them**: `../brainstorming/SKILL.md`, `../planning/SKILL.md`, `../execution/SKILL.md`, `../development-modes/SKILL.md` (or locate their installed equivalents). Skills are files, not a guaranteed call mechanism. Do not claim use of an unavailable skill or tool.

## Routes

**Light:** Infer or ask for only essential acceptance criteria → apply development mode → implement in main chat → run relevant checks → report real results. No separate brainstorm/plan documents or plan approval by default. If new evidence reveals substantial risk and complexity was `auto`, reclassify before continuing.

**Medium:** Interactive `brainstorming` (resolve **all** in-scope architectural/behavioral questions, always compare meaningful alternatives, **save design**) → `planning` (short structured tasks with AC IDs, **save separate plan**) → **one explicit plan approval** → `execution` in the **current main chat**, including verification. Brainstorm design `ready` is not approval.

**Complex:** Interactive, persisted `brainstorming` → `planning` (self-contained work packages with dependencies and checks, **save separate plan**) → **one explicit plan approval** → `execution` (dynamic delegation to subagents **sharing one working directory**, coordinated overlapping edits/build outputs, main-agent integration and verification) → **independent read-only review subagent** → adaptive fixes and rechecks. Never falsely report independent review if subagent tooling is unavailable.

**Design-only / plan-only:** Stop at the requested boundary. Planning requires a ready saved design; if missing, resolve the prerequisite or report the blocker rather than inventing it.

## Development mode

Apply `development-modes` at **every** complexity. Auto is test-first: prefer `tdd` for testable behavior and reproducible bugs, `normal` where appropriate; never auto-select `strict-tdd` or `prototype`. Manual mode applies throughout requested scope, including subagents. When TDD is selected but testing infrastructure is absent, **create it before behavior implementation**; if impossible, report a blocker, never silently downgrade.

## Approval, changes and evidence

- **One approval gate** for the final Medium/Complex implementation plan; never require separate design confirmation or ask again merely to start execution/delegate. Plan approval does not replace tool/host permissions.
- Planning/execution may change private implementation, work breakdown, dependencies and checks **without reapproval** while approved requirements, architecture, constraints, public behavior and ACs are intact. Material changes reopen only affected brainstorming decisions, revise affected plan sections and require **scoped** reapproval before impacted work resumes.
- Saved design/plan paths and revisions are the canonical recovery state. Only the main agent updates the plan; subagents return evidence. Preserve unrelated user changes.
- Keep exploration targeted, questions nonredundant, reports short, and subagent context narrow. Never fabricate file persistence, tests, review, approval or completion; report blockers and checks as **passed / failed / not run**.
