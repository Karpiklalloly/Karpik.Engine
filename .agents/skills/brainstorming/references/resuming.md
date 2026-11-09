# Resuming an existing brainstorm

Read this when the user says "continue", points to a saved brainstorm, returns after an interruption, or the workflow hands over a previously created design document.

## 1. Locate the authoritative document

Check in order:
1. An explicit user-provided document/path.
2. A path supplied by the invoking workflow or current task state.
3. Matching documents in the project's established brainstorm/design directory (default `docs/brainstorms/`). Match **topic and scope**, not just the most recent filename.
4. Usable prior conversation only if the document cannot be found.

If multiple documents plausibly match and the choice affects decisions, ask the smallest necessary disambiguation question. Never silently merge unrelated brainstorms. Do not invent previous decisions or a document path.

## 2. Recover and reconcile

- Read status, goal/scope, decided alternatives and rationale, acceptance criteria, open questions and resume point.
- Preserve explicit decisions. Do not repeat the entire design or ask for fresh confirmation of already settled matters.
- Current user instructions override older preferences. Changed requirements or verified changes in relevant code can invalidate dependent decisions, but **must not silently replace user choices**: explain the conflict, reopen affected questions, and update only dependent sections.
- Check code/configuration freshness **only where the design depends on project facts that may have changed** (for example changed interface/contract); do not scan the whole repository by default.
- If the document is incomplete/corrupt, reconstruct only supported facts from context and clearly distinguish unknowns from established decisions.

## 3. Resume according to status

- `in_progress`: briefly state where discussion stopped and continue with the highest-priority remaining question, using adaptive grouping where appropriate.
- `blocked`: revisit the specific blocker. If resolved, change to `in_progress` and proceed; otherwise request only the missing information or action and keep `blocked`.
- `ready`: if the workflow needs the result, hand the existing document to planning without fresh brainstorming or design approval. If asked to **change the design**, reopen only affected decisions, set `in_progress`, and resolve newly relevant questions before returning to `ready`.

If no saved document can be recovered, do not pretend restoration succeeded. Reconstruct what is available from the conversation, save a new `in_progress` document, and ask only for genuinely missing architectural choices.

## 4. Persist without duplication

- Continue writing to the **same file and original started-date filename**. Maintain consistent decision/question IDs.
- Save after significant new choices or before intentional pause. A sudden crash can still lose progress after the last checkpoint; do not imply otherwise.
- If a saved decision conflicts with current verified facts, record the conflict and new unresolved question rather than treating an obsolete decision as final.
- When all questions are closed, mark `ready` and verify the saved result. Never add a second approval solely for resumption.
