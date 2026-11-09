---
name: brainstorming
description: Interactively clarify requirements and design architecture and behavior before implementation. Use when the development workflow requests brainstorming, or when the user asks to explore or design a feature or subsystem. Resume prior discussions from their saved design document when available. Not for routine implementation or detailed task planning.
---

# Brainstorming

Turn an idea into a complete, implementation-independent architecture and behavior design through interactive discussion. **Resolve every question within scope, consider alternatives for every significant architectural decision, and persist one resumable design document.** Do not implement or write an execution plan.

## Workflow contract

- The invoking workflow determines whether brainstorming is needed and supplies complexity (`medium` or `complex`) when known. Explicit standalone use is also valid.
- Brainstorming determines **what to build, observable behavior, responsibilities, architectural interactions and constraints**. Planning determines **work items, concrete files, private classes/methods, implementation order, and detailed tests**.
- Discussion choices are decisions, **not an extra design-approval gate**. Never demand approval merely to finish brainstorming. The workflow performs the **single plan approval** after medium action points or a complex execution plan have been prepared.
- If called standalone, provide the saved result and stop. Do not start planning or implementation unless requested.

## Scope

Resolve questions about:
- Goal, users, scope boundaries, functional requirements and acceptance criteria.
- Main flows, observable errors, important edge cases, and behavior under failure.
- Architectural responsibilities, communication, data flow, interfaces and integration with existing systems.
- Public API/contract decisions when architecturally consequential; compatibility, security, performance and other significant constraints.
- Fundamental algorithm or data structure choices only where they affect architecture or external behavior.

Do not expand the requested scope without discussion. Leave routine implementation details and work breakdown to planning. **Every question within this scope must be resolved**; do not reclassify an architectural uncertainty as an assumption or postpone it just to finish.

## Procedure

1. **Recover state first when applicable.** If continuing an existing discussion, a document is provided, or context may have been compacted, read [references/resuming.md](references/resuming.md) and recover the current decisions before asking anything.
2. **Investigate relevant context.** Read the user request, decisions and pertinent project code/conventions. Find relevant entry points and contracts first; expand investigation only where necessary. Verify technical facts from available code/tools rather than asking the user. Clearly separate verified facts from hypotheses.
3. **Open a single document.** As soon as the task and topic can be identified, create or reuse its document according to [references/design-document.md](references/design-document.md), initially marked `in_progress`. Do not create duplicate documents when resuming.
4. **Track architectural questions.** Maintain a compact question register with `open`, `resolved` and `blocked` states. Reassess it when new evidence or requirements appear. Investigate technical uncertainties; ask the user about product intent, subjective preferences and material tradeoffs.
5. **Discuss interactively.** Start with the most consequential question. Default to one question per message; group independent, closely related questions only when this reduces turns without confusing answers. Do not ask a dependent question before its prerequisite is answered. Recommend a solution where supported, explain material tradeoffs, and never ask for a redundant confirmation.
6. **Always evaluate alternatives.** For every **significant architectural decision**, compare at least two feasible approaches if they exist (typically 2–3), their tradeoffs relative to the project, and a recommendation. If only one approach is viable, briefly explain why realistic alternatives fail. Do not invent alternatives for trivial details. Challenge technically weak suggestions with reasons instead of automatically agreeing.
7. **Record decisions and checkpoints.** Store selected approach, meaningful alternatives, brief rationale and impact. Update the same document after each consequential decision or coherent group of decisions; avoid a write after every minor conversational turn. Reopen a decision only upon changed requirements, new evidence or contradiction; never silently override a user decision.
8. **Audit completeness.** Verify applicable requirements, behavior/error cases, architecture/integrations, contracts, constraints, alternatives and acceptance criteria. Reconcile contradictory decisions. Continue discussion while any in-scope question is `open` or `blocked`.
9. **Finalize and hand off.** Only if `open = 0` and `blocked = 0`, save and verify the document as `ready`, then follow [references/handoff.md](references/handoff.md) to transfer its exact path, revision and acceptance criteria to the workflow. `ready` is not implementation approval; never request a separate design approval. If blocked or stopped early, persist `blocked` or `in_progress` with explicit unresolved questions and stop without claiming completion.

## Persistence and resumption

- **Exactly one durable document per brainstorming topic**, including unfinished discussions. Preserve its path across sessions. Follow [references/design-document.md](references/design-document.md) for location, template and status semantics.
- Save a checkpoint after each material architectural decision and before a deliberate pause, handoff or completion. This limits lost progress if the chat context disappears, without writing after every answer.
- On resume, use [references/resuming.md](references/resuming.md). Trust the document as a record of past choices, **not as proof that project code is unchanged**. Verify only potentially affected facts; do not unnecessarily re-audit the repository.
- If planning discovers a new architectural/behavioral issue, reopen only affected questions in the same document and follow [references/handoff.md](references/handoff.md). Keep the single plan-approval boundary.
- If filesystem writes fail, provide the full updated document in chat and explicitly state that persistence failed. Never claim a checkpoint was saved without verifying the write.

## Efficiency and quality

- Avoid repeating known questions, re-listing every prior decision or restating the entire plan in each reply.
- Prefer concise comparisons and targeted code exploration; load references and additional skills only when needed.
- Do not spawn subagents for routine brainstorming. Do not turn the discussion into implementation, a detailed task checklist or an extra approval ritual.
- Explicit user choices remain binding unless new facts reveal a conflict, in which case surface the conflict and resolve it interactively.
