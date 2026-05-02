# Architecture Decision Records

This folder stores durable architecture notes for Obsidian. ADRs explain why an important decision was made, what alternatives were rejected, and what consequences the project accepts.

Use ADRs for decisions that should remain understandable after the implementation context is gone:

- ECS data layout and ownership boundaries;
- Client / Server / Shared responsibility changes;
- network protocol, delivery, and authority decisions;
- hot reload state boundaries;
- dependency choices;
- performance or memory-safety trade-offs.

Do not use ADRs as task plans. Use `plans/PLANS.md` and a task-specific ExecPlan for active implementation work, then link the resulting ADR from the ExecPlan retrospective.

Start new ADRs from `docs/02_ADR/adr-template.md`.
