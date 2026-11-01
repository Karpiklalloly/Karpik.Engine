# Approval, revisions, adaptation and recovery

Read when presenting a plan for approval, recording an approval, adapting an approved plan, or resuming from disk.

## Plan state machine

| Status | Meaning |
|---|---|
| `draft` | Being prepared or paused before validation. |
| `awaiting_approval` | Validated, persisted, and offered for the *single* plan approval. |
| `approved` | User explicitly approved the plan; implementation may start via workflow. |
| `executing` | Main agent/execution is implementing the approved plan. |
| `completed` | All ACs and required verification/review are satisfied. |
| `blocked` | Concrete missing access/information/decision prevents progress; record next action. |
| `outdated` | Plan is no longer validated against the relevant source-design revision and needs impact review. |

Do not assign `approved` merely because a design is `ready`. A draft may remain a draft between sessions without being blocked. A plan may be `approved` yet need review after a source change; switch to `outdated` when material.

## Single approval boundary

1. Planning creates and validates the full medium/complex plan, saves it as `awaiting_approval`, and presents a concise summary.
2. Ask once for approval to implement **that plan**. User change requests amend the same pending proposal; they are not an approval and are not a new design approval stage.
3. After explicit affirmation, record:
   - `approved_plan_revision = plan_revision`
   - `approved_design_revision = design_revision`
   - `status = approved`
4. Handoff to workflow/execution includes exact `plan_path`, `plan_revision`, `design_revision`, `complexity`, `development_mode`, and approval information. The workflow may start execution with no additional approval about agent delegation or scheduling.
5. If the user only asks to **prepare** a plan, do not start implementation just because approval metadata exists; respect the task intent and the invoking workflow.

Never silently interpret ambiguous responses as approval. Do not request approval for reading code, saving the plan, or internal decomposition.

## Revisions and controlled adaptations

- `design_revision`: material architecture/behavior/constraint/acceptance changes in brainstorming; formatting and checkpoint writes do not change it.
- `plan_revision`: increment when work decomposition, dependencies, acceptance mapping, or verification strategy materially change. Do not increment for status/progress updates or formatting.
- `approved_plan_revision` and `approved_design_revision` record the last explicitly approved baseline. They may be older than current plan revisions **only when all subsequent changes preserve agreed scope, behavior, constraints and ACs**.
- Record significant execution adjustments concisely in `## Adjustments` with reason and whether renewed approval was needed. Do not journal every minor action.

Execution can autonomously reorder, split, combine, or add technical tasks, add checks, and revise private implementation details, provided the design, user constraints, approved scope and acceptance criteria remain intact. Execution owns runtime scheduling and may override planning's hints about conflict risk after checking the actual code.

**Requires returning to brainstorming / reapproval:** materially changed public behavior, API/architecture, requirement, design constraint, or AC; or a requested scope change. Reopen only affected design questions, re-evaluate feasible alternatives, persist the updated ready design with a new revision, adjust affected plan sections, and ask approval of the **affected revised plan**. Avoid reapproving unrelated completed work.

## Planning errors and blockers

- Ordinary implementation uncertainty (file locations, private class layout, dependency cycle, task boundaries, how to test) belongs to planning; investigate and fix locally.
- A design contradiction or missing significant architecture/behavior decision belongs to brainstorming; do not quietly assume a resolution.
- Access failure or unavailable external specifications are `blocked` only when they actually stop planning. Record the blocker, evidence, and exact action required. If persistence fails, tell the user; do not claim the file is saved.
- If the selected development mode is infeasible, surface concrete evidence and options. Never switch from `strict-tdd` to `normal` without user approval.

## Resume procedure

1. Locate the authoritative plan path from the workflow/user or matching task; don't pick an unrelated recent document.
2. Read the plan, current status, approved baseline, source design file and revision, task statuses and the latest meaningful adjustments.
3. Compare the stored `design_revision` with the actual design revision; if changed, examine affected decisions/ACs rather than blindly invalidating all tasks. Mark `outdated` if material discrepancies remain unresolved.
4. Check relevant repository state, changes and test evidence before trusting `completed` items; preserve valid completed work and do not rerun it solely because a new chat started.
5. Resume according to status: draft → continue planning; awaiting_approval → show existing plan for approval; approved/executing → execution continues with the approved baseline; blocked → revisit precise blocker; outdated → reconcile design and plan, obtain approval if material changes affect agreed behavior.
6. Only **the main agent** edits the shared plan file and statuses. Subagents return results and evidence; main agent decides whether each package is complete. Avoid concurrent document writes.

Do not assert that a code change, test run, review, checkpoint or approval happened without direct evidence.
