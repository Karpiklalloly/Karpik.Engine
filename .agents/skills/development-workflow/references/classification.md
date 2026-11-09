# Complexity classification

Use when the user's complexity selection is `auto`, or when planning discovers substantial new facts. This is a **judgment**, not a point-score or file-count threshold.

| Level | Typical characteristics | Default workflow |
| --- | --- | --- |
| `light` | Clear behavior, localized safe change, known API, no important architectural choices; can be validated directly. | Criteria → implement → verify. |
| `medium` | A new bounded feature or multiple related changes; choices about architecture/behavior warrant discussion, but one main agent can implement and integrate it. | Interactive brainstorm → structured plan → one approval → main-agent execution. |
| `complex` | Substantial new subsystem/cross-cutting change, consequential architectural uncertainty, difficult integration/compatibility, high potential impact, or several worthwhile independent workstreams. | Interactive brainstorm → work-package plan → one approval → dynamic subagents/integration → independent review. |

## Decision rules

1. Start from the requested outcome and uncertainty, inspect **only relevant** project code and constraints before final commitment.
2. Architecture and risk matter more than file count or estimated lines. A tiny change to a network protocol or save format can be complex; large generated changes can be light.
3. Prefer `light` if criteria and implementation are truly obvious and low-risk. Choose `medium` if important choices need joint discussion. Choose `complex` when architecture/integration risk or useful independent work packages justify it; don't select Complex merely to spawn agents.
4. If in doubt between adjacent levels, choose the less costly level **only if** it won't miss required architectural discussion, approval or independent verification. Raise as soon as material contrary evidence appears.
5. If selected automatically, Planning may raise/lower classification and update the plan accordingly. If user selected a level manually, **preserve it** and explain any blocker or safety/feasibility conflict rather than changing levels invisibly.
6. Complexity and development mode are orthogonal: `light + strict-tdd` and `complex + prototype` are both valid.

## Manual controls

Interpret explicit instructions such as `complexity: complex` or `development: strict-tdd`; do not require special syntax. Treat a later user selection as overriding an earlier project/default selection. If a plan already exists, reconcile the changed selection with the approved scope and actual implementation state rather than restarting blindly.
