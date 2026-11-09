# Plan execution state, recovery and completion

Read when starting/resuming execution, adapting work items, recording acceptance, or handling blockers.

## Preconditions and identity

- Locate the exact plan from user/workflow, or unambiguously match its title and source design; do not select the most recent unrelated plan.
- Read YAML frontmatter: `status`, `complexity` (`medium`/`complex`), `development_mode`, `source_design`, `design_revision`, `plan_revision`, `approved_plan_revision`, `approved_design_revision`.
- Read the saved design: it must be `ready`, with matching relevant revision/constraints/AC IDs. If design content materially differs from the approved baseline, stop affected work for impact review, brainstorming and reapproval. A mere plan revision mismatch may be permitted under controlled technical adaptation.
- Refuse to interpret `ready` design or `awaiting_approval` plan as authorization. Only a plan with a recorded approval baseline and `approved`/`executing` status can start/resume. Verify explicit user approval if history shows a conflict.
- Preserve existing user changes. Inspect `git status`/relevant diffs or equivalent; distinguish work from this execution versus unrelated edits; never revert or overwrite unfamiliar changes without consent.

## Canonical statuses

Plan: `draft`, `awaiting_approval`, `approved`, `executing`, `completed`, `blocked`, `outdated` (as defined by planning).

Task/package: `pending`, `in_progress`, `completed`, `blocked`, `superseded`. `completed` means main-agent acceptance after inspecting output and evidence, not just a subagent claim.

- Only main agent updates the one plan file. Keep the actual task/package sections canonical; don't maintain a conflicting shadow status file.
- On beginning execution, write `status: executing`. If a blocker prevents any meaningful progress, use `blocked` with reason and recovery action. If the design becomes materially invalid, use `outdated` until reconciled.
- Persist meaningful checkpoints: package acceptance, dependency updates, adaptation, review outcome, blocker, completion. Do not write per command or fabricate timestamps/results.

## Controlled adaptation

- Allowed without reapproval: reorder or split/merge packages, add technical tasks, improve tests, change private implementation, reassign subagents, and correct dependencies, **provided** requirements, architecture, constraints, scope and ACs remain intact.
- Increment `plan_revision` for material work-breakdown, dependency, AC-mapping or verification-strategy changes; do not increment for ordinary status changes or formatting. Keep `approved_plan_revision` unchanged for compatible adaptations. Summarize significant adjustments with a reason and why new approval was not required.
- Reuse stable IDs when possible; mark replaced items `superseded` and link to replacements instead of reusing one ID for a different purpose. Check for dependency cycles and referential integrity after changes.
- If change affects approved architecture/public behavior, scope, constraints or ACs: stop dependent implementation, reopen only impacted design decisions in brainstorming, update impacted plan sections through planning and obtain approval of the affected revised plan before proceeding. Preserve unrelated accepted packages.

## Recovery algorithm

1. Read latest plan, source design, approved baseline, task statuses, open blockers/findings and meaningful adjustments.
2. Check repository state and targeted tests to establish which completed work actually exists. Do not rerun heavy tests without a reason; previous logs are evidence only if their relationship to current code is clear.
3. Reconcile `in_progress` items whose subagent/context is gone: inspect their changes, return to `pending` if unaccepted, or finish verification and accept if evidence suffices.
4. Recompute ready items from dependencies; continue in current chat for Medium, or schedule safe subagents for Complex.
5. If subagent tools/reviewer unavailable, keep an explicit blocker instead of falsely reporting full compliance. If permissions or missing information prevent safe progress, record the minimal required next action.

## Completion gate

Before setting `status: completed`, verify:

- Every current task/package is accepted, or legitimately superseded by accepted replacement(s).
- Every design AC is fulfilled and supported by relevant evidence; none silently dropped.
- Global/integration checks ran and passed, including relevant test/build checks for current code. If a required check cannot run, remain non-completed and explicitly state the limitation.
- For Complex, a separate review subagent examined the integrated outcome; all confirmed blocking issues are fixed and rechecked. A review that merely *started* is not sufficient.
- Report any nonblocking risks and skipped optional checks honestly. User-requested stop/cancellation yields a saved partial state, not fabricated completion.

## User-facing handoff

Keep the final response compact: summary of actual changes, checks (`passed`/`failed`/`not run`), independent review result for Complex, unresolved issues and exact persisted plan location/status. Do not request a second implementation approval after approved plans.
