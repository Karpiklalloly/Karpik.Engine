# Routing, handoffs, approval and resumption

## Resolve task intent first

- `implement`: run the light/medium/complex route unless a suitable approved plan is already ready to resume.
- `design-only` or explicit brainstorming: run only the brainstorming skill; produce/update its saved document, do not create an implementation plan or write code.
- `plan-only`: run planning only if a ready design exists; otherwise finish required brainstorming first. Save and present the plan but do not automatically execute it. If the user explicitly forbids earlier phases, report the missing prerequisite.
- `review/analysis-only`: perform requested review/analysis; do not force a code-change workflow.
- `resume`: find matching saved artifacts and continue from actual status and user intent. Do not interpret the most recent unrelated file as the target.

## Phase handoffs

| Sender | Receiver | Minimal data / gate |
| --- | --- | --- |
| Workflow | Brainstorming | Goal/scope, user choices, auto/manual complexity, project context |
| Brainstorming | Planning | **Exact saved design path**, `Revision`, `Status: ready`, decision IDs and stable acceptance IDs; no unresolved in-scope questions |
| Planning | Workflow | **Exact saved plan path**, source design/revision, plan revision, complexity/source, effective development mode, `awaiting_approval` |
| Workflow | Execution | Plan path; saved `approved_plan_revision` and `approved_design_revision`; plan `approved`/`executing` state; user requested implementation |
| Execution | Workflow | Actual task statuses, integration/verification evidence, review evidence for Complex, blockers; same updated plan |

Treat plans and design docs as the canonical durable artifacts. Do not copy their complete text into every message or subagent prompt. Do not invent an independent state/approval file.

## Exactly one approval gate

- Brainstorming is **interactive**, including comparison of meaningful alternatives. User's individual choices resolve questions but are **not** a second design approval stage.
- The plan must be validated and persisted before requesting explicit permission to implement. Show a concise overview of scope, tasks or work packages, meaningful dependencies, risks and checks.
- A request to modify a plan keeps the same approval conversation open; ask for plan approval only after incorporating the changes. Ambiguous acknowledgments are not approval.
- Record approval in the plan (approved plan and design revisions), then execution starts in the same conversation without asking again for scheduling, subagents or start permission.
- Separate tool permissions still apply. New architecture/requirements/ACs later trigger **scoped** revision and approval; purely technical adaptation does not.

## Resumption by artifact state

1. Identify the exact matching task from user-provided path or topic, then read the existing design and/or plan; verify current code only where relevant.
2. Design `in_progress` or `blocked`: resume brainstorming from unresolved questions. Design `ready` but no plan: route planning (unless design-only was requested).
3. Plan `draft`: complete planning; `awaiting_approval`: show existing plan and obtain approval **only if implementation is wanted**; `approved` or `executing`: resume execution without reapproval when the approved baseline remains applicable.
4. Plan `outdated`: reconcile only affected design and plan sections; seek approval for material changes. Plan `blocked`: revisit the specified blocker. Plan `completed`: report current state or handle newly requested work as a distinct task.
5. Preserve saved dates/paths, revisions and valid completed items. Do not trust stale completion flags blindly; inspect pertinent code and evidence.

If a required subagent/reviewer isn't available in Complex, never counterfeit delegation or review. Surface the actual blocker and possible options, but don't silently weaken a manually selected workflow.
