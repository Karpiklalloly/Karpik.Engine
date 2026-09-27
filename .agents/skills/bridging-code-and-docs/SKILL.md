---
name: bridging-code-and-docs
description: Use when debugging, modifying, extending, or refactoring an existing codebase where project documentation, specifications, ADRs, architectural decisions, or previous design decisions may affect the correct implementation.
---

# Bridging Code and Docs

## Purpose

Coordinate source-code intelligence from understand-anything with project-documentation
retrieval from QMD.

Do not replace Superpowers workflows.

Superpowers owns:

- brainstorming;
- design/spec creation;
- implementation planning;
- debugging methodology;
- TDD;
- review;
- verification;
- execution workflow.

This skill only decides what project context should be retrieved and how
documentation should be related to the current implementation.

## Source roles

Treat each source differently:

- User request = desired behavior.
- Source code and tests = current implemented behavior.
- Project documentation/specs/ADRs = intended behavior, requirements,
  architecture and historical design decisions.
- understand-anything = generated graph of source structure and relationships at an analyzed point in time.
- QMD = retrieval/index over project documentation.

Documentation is NOT proof of current runtime behavior.

The graph may omit recent code changes or relationships it could not infer. A
graph edge is NOT proof that a path is exercised at runtime.

Actual source, tests and runtime evidence must be inspected when correctness
depends on them.

If documentation and implementation disagree, do not silently choose one.

Determine whether:

1. implementation is wrong;
2. documentation is stale;
3. requirements changed;
4. both require an update.

## understand-anything policy

Use an existing understand-anything graph when structural context would help.
It is stored in `.ua/knowledge-graph.json` (or the legacy
`.understand-anything/knowledge-graph.json`). Before relying on it, check its
commit metadata against the current repository and account for staged,
unstaged and untracked changes. A graph at `HEAD` can still miss worktree edits.

Typical triggers:

- debugging;
- new behavior;
- feature changes;
- refactoring;
- unfamiliar subsystem;
- dependency analysis;
- impact analysis;
- lifecycle/ownership;
- changes spanning multiple symbols/files;
- architecture-sensitive changes.

Choose the operation that matches the question:

- `understand-anything:understand-chat` for finding related components and
  exploring graph relationships;
- `understand-anything:understand-explain` for a specific file, function or
  module, alongside its actual source;
- `understand-anything:understand-diff` for impact analysis of existing changes;
- `understand-anything:understand` to create or refresh the graph when the
  existing graph is missing or materially stale and the analysis is useful.
  Prefer its incremental update; do not force a full rebuild for routine edits.

Use graph nodes and edges to suggest:

- relevant symbols;
- callers/callees;
- dependencies;
- type relationships;
- call paths;
- affected areas;
- related implementation.

These are leads, not a complete caller list or an exact control-flow trace.
Inspect the relevant source and tests; verify exact callers and change impact
against the current checkout before editing.

Skip graph exploration for clearly localized mechanical work:

- typo fixes;
- formatting;
- replacing a known literal;
- renaming a local variable;
- editing an explicitly known isolated location with no behavioral impact.

If the graph is missing, stale or lacks the relevant symbol, proceed with
targeted source inspection. Do not block the task on graph generation.

## QMD policy

Do NOT query QMD for every code change.

Use QMD when one or more conditions apply:

- architecture is involved;
- subsystem boundaries are involved;
- public API is changing;
- behavioral contracts are changing;
- lifecycle or ownership is involved;
- persistence is involved;
- serialization is involved;
- protocols are involved;
- file formats are involved;
- multiple subsystems are affected;
- the user references previous design/decision/specification;
- understanding WHY something was designed this way matters;
- the graph suggests a non-obvious architectural pattern;
- the change may invalidate existing documentation;
- debugging suggests current behavior may differ from intended behavior.

Skip QMD for:

- trivial local bugs with an obvious cause;
- syntax errors;
- simple compiler errors;
- formatting;
- mechanical edits;
- changes without architectural or behavioral implications.

## Hybrid retrieval workflow

For non-trivial work:

1. If an applicable graph exists, check freshness and query it for candidate
   components or affected areas. Otherwise locate them in the current source.
2. Identify important concepts:
   - subsystem names;
   - types;
   - methods;
   - interfaces;
   - architectural terminology.
3. Decide whether QMD is necessary.
4. If QMD is necessary, search using:
   - conceptual terms from the user's task;
   - subsystem terminology;
   - important symbols discovered through the graph or source.
5. Retrieve only the most relevant documents/sections.
6. Map documentation claims back to current source and tests; use graph
   relationships to find related code when they are available and current.
7. Verify behavior and change impact from source, tests or runtime evidence.
8. Continue with the active Superpowers workflow.

The desired model is:

    understand-anything = a navigable snapshot of possible code relationships
    QMD = what was intended/decided/documented
    Superpowers = how to perform the engineering work
    Bridge = when/how to combine those contexts

## Debugging behavior

For bugs, use the graph to suggest where to look:

- Where can this behavior originate?
- What calls this code?
- What does it call?
- What depends on this symbol?
- What is the relevant control/data flow?
- What else could be affected?

QMD should be consulted only when relevant to answer:

- What behavior was intended?
- Is this lifecycle documented?
- Is this architecture documented?
- Was there a previous design decision?
- Why was this implemented this way?

Always establish the root cause from actual implementation/runtime evidence
before applying a fix.

## Feature/refactoring behavior

For a feature/refactor in an established subsystem:

1. Use a current graph, when available, to find related components and likely
   impact. Verify against the checkout; use `understand-anything:understand-diff`
   when there are existing changes to analyze.
2. Determine if there may be related design/ADR/spec context.
3. If yes, query QMD.
4. Feed the resulting context into the normal Superpowers
   brainstorming/planning workflow.
5. Do not create a second parallel planning methodology.

## Documentation maintenance

After implementation and verification:

- update authoritative docs if intended behavior changed;
- fix docs discovered to be stale;
- update ADR only if the architectural decision itself changed;
- do not rewrite unrelated documentation;
- do not duplicate an existing Superpowers spec unnecessarily.

If documentation changed:

- update the QMD collection/index;
- update embeddings for changed documents when required.

## Efficiency

Avoid duplicate work.

- QMD is not a second source-code search engine.
- Graph document nodes are summaries; use QMD and original documents for
  documentation claims.
- Use graph navigation for candidate relationships, then inspect only the
  relevant current code. Do not treat absent edges as proof of no dependency.
- Prefer one broad QMD semantic query followed by targeted retrieval.
- Retrieve specific sections instead of whole documents where possible.
- Do not load large amounts of context without reason.
- Do not query QMD if documentation is clearly irrelevant.
- Refresh the graph when its staleness affects a decision, not after every edit.

The goal is the minimum context necessary for a correct engineering decision.
