---
title: "<short decision title>"
date: "YYYY-MM-DD"
status: "proposed"
tags:
  - adr
  - architecture
---

# <Short Decision Title>

> Status: proposed | accepted | superseded | rejected
> Date: YYYY-MM-DD
> Owners: <people or agent/thread>
> Related ExecPlan: [[../../plans/<name>-execplan]]

## Context

Describe the problem and the constraints that matter. Include enough repository context for this note to make sense in Obsidian without reopening the implementation chat.

For runtime decisions, state the real-time constraints: hot path, allocation budget, data layout, cache locality, side boundaries, tick behavior, concurrency, and validation expectations.

## Decision

State the decision directly. Use concrete repository terms: modules, projects, systems, components, protocols, or files.

## Alternatives Considered

Describe the meaningful alternatives and why they were rejected.

## Consequences

Describe the expected benefits, trade-offs, risks, and maintenance costs.

Include performance and memory-safety implications when relevant.

## Validation

List tests, benchmarks, manual scenarios, or proof-of-concept evidence that support the decision.

## Links

- ExecPlan: [[../../plans/<name>-execplan]]
- Related docs:
- Related code:
