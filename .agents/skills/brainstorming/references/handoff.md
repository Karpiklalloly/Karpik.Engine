# Brainstorming handoff to planning

Read when a brainstorm is ready, or when planning/execution reopens a design issue.

## Ready design → planning

1. Check all in-scope questions are resolved, including blockers, and `Open questions / blockers` says `None`. Verify meaningful alternatives, architectural choices, constraints, and acceptance criteria are captured.
2. Save the **same** document with `Status: ready` and a current `Revision` (an integer). All acceptance criteria must have stable IDs (`AC1`, `AC2`, ...); significant decisions should have stable IDs (`D1`, ...). Do not rewrite IDs solely to reorder sections.
3. Verify the saved file exists. Pass planning the **actual path**, `Revision`, objective/scope, and stable criterion IDs, plus auto/manual complexity and development-mode settings if known. Planning reads the document itself; do not duplicate the full design in another prompt.
4. `ready` means *ready to plan*, **not approval to implement**. Do not request design signoff. The workflow obtains exactly one approval of the completed implementation plan.

## New architectural issue discovered later

1. Stop only work dependent on the affected decisions. Return to the existing design document; change status to `in_progress` and reopen precisely affected questions.
2. Discuss alternatives interactively and resolve all in-scope questions. Recheck dependent constraints and ACs, preserving unaffected decisions.
3. Increment `Revision` only if the accepted design materially changed. Save it as `ready` again, verify the file, then tell planning to revise affected tasks/work packages.
4. If an implementation plan was already approved, obtain approval **only for the materially changed part** of the plan before impacted execution resumes. Preserve valid completed work and do not require a second design approval.

If file persistence fails, report the failure explicitly and do not claim an authoritative saved handoff.
