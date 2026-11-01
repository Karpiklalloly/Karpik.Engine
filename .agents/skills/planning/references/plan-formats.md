# Plan document format

Read when creating or editing a medium/complex plan. Plans are Markdown with concise YAML frontmatter. The design document is the architectural source of truth.

## Location and identity

- Reuse an existing plan for the same task and source design; maintain its path across sessions.
- Prefer the repository's existing plan directory; otherwise use `docs/plans/YYYY-MM-DD-<short-kebab-case-topic>.md` under project root. For a non-project task, use a writable work directory.
- Use the date planning began and retain it during resumption. Never overwrite an unrelated plan.
- Save a plan before asking for approval, even if work is interrupted. Keep a *single* current plan per task.

## Common frontmatter

```yaml
---
status: draft                 # draft | awaiting_approval | approved | executing | completed | blocked | outdated
complexity: medium           # medium | complex
complexity_source: auto      # auto | manual
# Exact chosen value; 'auto' is resolved before approval.
development_mode: tdd        # normal | tdd | strict-tdd | prototype
source_design: ../brainstorms/YYYY-MM-DD-topic.md
design_revision: 1
plan_revision: 1
approved_plan_revision: null
approved_design_revision: null
---
```

If metadata is unknown, resolve it from workflow/user instructions instead of guessing. Keep source paths valid relative to the plan file or use a clear absolute path. Set approved revision fields only after explicit approval. Do not conflate `ready` design status with approved plan status.

## Medium: structured tasks

Use a short list of tasks executed by the **main agent in the current chat**; do not embed package-style role assignments. Goal and AC mapping are mandatory; dependencies and task-specific verification are optional.

```markdown
# ECS RPC — implementation plan

## Summary
<One or two sentences; no duplicate architecture specification>

## Tasks

### T1 — RPC contracts
- Status: pending
- Goal: Implement the contracts defined in D1.
- Acceptance: AC1

### T2 — Serialization
- Status: pending
- Goal: Implement RPC argument/result serialization.
- Acceptance: AC2
- Depends on: T1
- Verification: Round-trip and malformed-payload tests.

## Global verification
- Confirm all acceptance criteria, including integration and failure paths.
- Run relevant build, test and project checks.

## Risks / blockers
- None known.

## Adjustments
- None.
```

Use stable IDs (`T1`, `T2`) across revisions. Each task should yield one meaningful, verifiable result; don't make a separate task for each private method or file. Explicit AC coverage may include the global verification section if the criterion is inherently end-to-end.

## Complex: autonomous work packages

Each work package is suitable to delegate without repeated clarification. Package scope includes explicit boundaries, and verification is mandatory. Dependencies are explicit even when empty. Don't bind packages to named agents, scheduling waves or concurrency groups: execution decides these at runtime.

```markdown
# ECS RPC — implementation plan

## Summary
<Short goal and scope, referring to the design document>

## Work packages

### WP-01 — RPC contracts
- Status: pending
- Goal: Provide the shared RPC contracts established in D1.
- Scope: Shared public/internal contracts needed by downstream packages.
- Boundaries: No transport or serialization implementation.
- Depends on: none
- Acceptance: AC1
- Verification: Compile public contract usages and run contract tests.

### WP-02 — Serialization
- Status: pending
- Goal: Implement the serialization path using WP-01 contracts.
- Scope: Argument and result conversion, malformed payload handling.
- Boundaries: Do not change WP-01 contracts without escalation.
- Depends on: WP-01
- Acceptance: AC2, AC3
- Verification: Round-trip and malformed-payload tests.
- Integration notes: Possible shared-contract touchpoints with WP-03.

## Global integration and verification
- Integrate package outputs and check end-to-end acceptance criteria.
- Run relevant build, test and project checks.

## Independent review
- After integration, delegate a separate reviewer to compare changes with the saved design, plan, ACs and constraints.
- Main agent triages findings, fixes issues, and rechecks as needed.

## Risks / blockers
- None known.

## Adjustments
- None.
```

Use stable `WP-xx` IDs. Split work into cohesive units with minimal overlap; package count is driven by actual responsibilities, not arbitrary granularity. A work package can be sequential or parallel: only dependencies and conflicts matter to the plan. If an acceptance criterion can only be checked end-to-end, map it explicitly in the global integration section instead of falsely claiming it is package-local.

## Validations required before `awaiting_approval`

- Every AC ID from the design is referenced in a task/package or explicit global integration criterion; none is silently dropped or renumbered.
- Dependencies reference existing IDs and form a directed acyclic graph; a package never depends on itself.
- Goals and criteria allow completion to be verified.
- Medium stays compact; complex units have defined boundaries and individual checks.
- Global verification exists, and complex includes independent review **after** integration.
- The plan references the exact current source design revision and preserves its constraints.
- The plan contains no invented verification results, no unnecessary implementation microsteps, and no preassigned execution schedule.

## Status of tasks / packages

`pending`, `in_progress`, `completed`, `blocked`, or `superseded`. Only the main agent updates these based on observed execution evidence; `completed` means accepted by the main agent, not merely claimed by a subagent.
