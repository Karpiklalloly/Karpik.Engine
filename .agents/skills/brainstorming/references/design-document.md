# Brainstorm document and checkpoints

## Location and identity

- Reuse an existing document for the same task before creating another. Prefer a known document path or workflow pointer; otherwise search the project's established design/brainstorm folder.
- If no relevant document exists, prefer an established project directory; otherwise use `docs/brainstorms/` under the project root. For non-project tasks, use a writable working directory.
- Default filename is `YYYY-MM-DD-<short-kebab-case-topic>.md`, using the date the discussion started. On resumption, **retain the original date and path**. Do not overwrite an unrelated document; add a suffix if needed.
- Create the document once the goal and scope can be identified; keep updating that same document.

## Status semantics

- `in_progress`: a normal active or paused discussion with unresolved in-scope questions. This is not an error.
- `blocked`: further progress currently requires unavailable information, access, or a decision the user cannot yet make. List the specific blocker and action needed.
- `ready`: **all** in-scope questions resolved, accepted decisions consistent, and verifiable acceptance criteria defined. Both open and blocked question counts must be zero.

An ordinary pause or chat interruption is `in_progress`, not `blocked`. An early explicit stop is `in_progress` unless a real blocker exists. Never label an unfinished document `ready`.

## Minimum document format

```markdown
# <Topic>

- Status: in_progress | blocked | ready
- Started: YYYY-MM-DD
- Updated: YYYY-MM-DD
- Revision: 1
- Complexity: medium | complex | other/unknown

## Goal and scope
<Problem, goal, what is included/excluded>

## Requirements and behavior
<Functional requirements, observable behavior, errors and edge cases>

## Architecture and integration
<Responsibility boundaries, interactions, data flow and significant public contracts>

## Constraints
<Compatibility, performance/security needs and verified project constraints>

## Decisions and alternatives
- D1 — <topic>: **<chosen option>**
  - Alternatives: <feasible approaches and meaningful tradeoffs>
  - Rationale: <why this fits the actual constraints>

## Acceptance criteria
- AC1 — <Observable/verifiable outcome>

## Open questions / blockers
- Q1 [open] — <precise question and why it matters>
- Q2 [blocked] — <missing information or action required>

## Resume point
<Next relevant question or investigation, without retelling the discussion>
```

- IDs `D1`, `AC1`, `Q1`, etc. are local, stable references within the document; keep them when updating related items.
- Start `Revision` at `1`; increment only when accepted requirements, behavior, architecture, constraints, decisions or acceptance criteria materially change, **not** for status/checkpoint/formatting updates. Planning uses the exact revision as its source baseline. Remove or mark resolved questions once settled; do not manufacture unresolved questions.
- When `ready`, use `None` in `Open questions / blockers` and `Resume point` (or state that planning is next). Keep the document self-contained; record concise reasons, not raw conversation transcripts or copied code.
- Clearly label any project fact that has not yet been verified. Do not record a disputed architectural preference as an accepted decision.
- The checkpoint should reflect the **latest meaningful resolved decision** and next unanswered question, not merely the initial prompt.
- Verify actual existence and latest content after saving; report the exact path on completion, blockage, or intentional pause.
