# KarpikEngine Execution Plans (ExecPlans)

This file defines the required format for an ExecPlan: a living design and implementation document that a coding agent can follow to deliver a working feature, refactor, investigation, or system change.

This local standard follows the OpenAI Cookbook guidance for `PLANS.md` and adapts it to KarpikEngine's real-time, ECS, client/server, and low-allocation constraints.

## When to Use an ExecPlan

Use an ExecPlan for:

- complex features that span multiple modules;
- significant refactors;
- networking, hot reload, ECS, rendering, job system, or serialization changes with unclear risk;
- multi-hour investigations;
- work that needs proof-of-concept milestones before full implementation;
- changes where another agent or developer may need to resume from the plan alone.

Do not use an ExecPlan for tiny, localized edits where the change and validation are obvious.

## How to Use ExecPlans

When authoring an ExecPlan, read this entire file first. The plan must be self-contained: a future reader should be able to continue from only the current working tree and the ExecPlan file.

When implementing an ExecPlan, keep it updated as work proceeds. Do not leave progress, discoveries, or decisions only in chat. If the implementation changes direction, update the plan before or alongside the code change.

When discussing an ExecPlan, record durable decisions in its `Decision Log`. The plan is not a static proposal; it is the project memory for that long-running task.

## Non-Negotiable Requirements

Every ExecPlan must:

- be fully self-contained;
- be updated as a living document;
- define non-obvious terms in plain language;
- describe user-visible or developer-visible outcomes;
- name repository-relative files and modules explicitly;
- include validation commands and expected observations;
- include recovery or rollback notes for risky steps;
- maintain `Progress`, `Surprises & Discoveries`, `Decision Log`, and `Outcomes & Retrospective`;
- respect KarpikEngine real-time constraints: no frame allocations, no avoidable pointer chasing, no cross-side project leaks, and no unvalidated hot-path changes.

## KarpikEngine-Specific Checks

Every ExecPlan that touches runtime code must include a short real-time assessment:

- Hot path: Is any edited code called from `Update`, `FixedUpdate`, ECS `Run`, serialization loops, network pumps, or render loops?
- Allocation budget: What allocations are expected? If non-zero in hot path, why is that acceptable?
- Data layout: Are data accesses linear and cache-friendly? Is Dragon ECS or dense array storage more appropriate than object graphs?
- Side boundary: Does the change preserve Client / Server / Shared separation?
- Tick behavior: Does physics or gameplay logic use fixed dt where required?
- Concurrency: Are locks, waits, atomics, jobs, or shared buffers used safely?
- Validation: What tests, benchmarks, allocation checks, or manual scenarios prove the change works?

For network changes, also state delivery semantics, serialization compatibility, throttle/batching behavior, and authority boundaries.

For ECS changes, also state component layout, aspect/filter strategy, pool access pattern, and whether data is mutable or read-only.

For hot reload changes, also state what state survives reload, what is disposable, and how repeated reloads are tested.

## File Location and Naming

Store task-specific ExecPlans under `plans/`.

Use short, descriptive lowercase names:

- `plans/<feature-or-refactor>-execplan.md`
- `plans/network-replication-execplan.md`
- `plans/ecs-physics-refactor-execplan.md`

`plans/PLANS.md` is the source of truth for all new plans. Existing `plans/new-*.md` files are legacy references only; do not start new work from them unless the user explicitly asks to preserve that older format. If older templates contain useful module-specific questions, copy the relevant ideas into a self-contained ExecPlan instead of depending on the old template.

## Formatting

An ExecPlan stored as a Markdown file should be plain Markdown, not wrapped in an outer code fence.

Use prose-first sections. Lists are welcome when they improve precision. The `Progress` section must use checkboxes. Commands must include the working directory and exact command line.

Avoid nested fenced code blocks when possible. If command output or code examples are needed, keep them short and focused.

## Required Skeleton

Use this skeleton for every ExecPlan and fill in every section.

```md
# <Short, action-oriented title>

This ExecPlan is a living document. It must be maintained according to `plans/PLANS.md`.

## Purpose / Big Picture

Explain what this change enables and how someone can see that it works.

## Progress

- [ ] (<timestamp>) Initial plan created.

## Surprises & Discoveries

- Observation: None yet.
  Evidence: Not applicable.

## Decision Log

- Decision: Initial approach.
  Rationale: Explain why this plan starts with this design.
  Date/Author: <timestamp> / <name or agent>

## Outcomes & Retrospective

No outcome yet. Update this after each major milestone and at completion.

## Context and Orientation

Describe the relevant repository state from scratch. Name files and modules by repository-relative path. Define any non-obvious terms.

## Real-Time Assessment

State whether the work touches hot paths. Describe expected allocations, data layout, side boundaries, tick behavior, concurrency risks, and validation strategy.

## Plan of Work

Describe the sequence of edits and additions in prose. For each edit, name the file and the function, type, or module to change.

## Milestones

Describe each milestone as a verifiable step with the behavior or evidence expected at the end.

## Concrete Steps

List exact commands with working directory. Include short expected outputs when useful.

## Validation and Acceptance

Describe tests, benchmarks, manual scenarios, and expected results. Acceptance must be observable.

## Idempotence and Recovery

Explain which steps are safe to rerun. Describe rollback or retry steps for risky operations.

## Artifacts and Notes

Link or name any generated artifacts, prototypes, benchmarks, logs, or follow-up plans.
```

## ADR Handoff

ExecPlans are working documents. ADRs are durable architecture notes for reading later in Obsidian.

When an ExecPlan results in an important architecture decision, create or update an ADR under `docs/02_ADR/` before closing the plan. Do this only for decisions that are worth preserving after the implementation context is gone, such as:

- choosing an ECS data layout or ownership boundary;
- changing Client / Server / Shared responsibilities;
- changing network protocol, delivery semantics, or authority model;
- changing hot reload boundaries or state persistence rules;
- adopting or rejecting a major dependency;
- accepting a measurable performance/safety trade-off.

Do not create an ADR for routine implementation details. In the ExecPlan `Outcomes & Retrospective`, link the ADR that records the final decision.

## Implementation Discipline

Proceed milestone by milestone. At every stopping point, update `Progress` with what is done and what remains. If new evidence changes the design, add it to `Surprises & Discoveries` and record the decision in `Decision Log`.

Validation is mandatory. A completed ExecPlan without executed tests, benchmarks, or a documented reason why they could not run is incomplete.
