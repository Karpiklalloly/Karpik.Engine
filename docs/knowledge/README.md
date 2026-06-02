# KarpikEngine Knowledge Base

This folder stores compact reusable project knowledge for humans and agents.

Use it for findings that should survive a chat thread but are not large enough or stable enough for an ADR:

- investigation results;
- recurring implementation patterns;
- debugging notes;
- postmortems;
- short lessons learned after substantial tasks.

Do not use this folder for active task planning. Use `plans/PLANS.md` and task-specific ExecPlans for work in progress.

Do not use this folder for architecture decisions that should be accepted, superseded, or rejected over time. Use `docs/02_ADR` for durable decisions.

## Layout

```text
docs/knowledge/
  investigations/  # debugging and research findings
  patterns/        # recurring implementation rules and local idioms
  postmortems/     # completed incident or regression analysis
  templates/       # note templates
```

## Agent Workflow

After a large task, investigation, or architectural discussion, an agent should propose 3-7 short learnings that may be worth recording here.

The agent must not write knowledge notes automatically. The developer decides which learnings become repository knowledge.

Keep notes small and searchable. Prefer concrete repository terms: project names, modules, systems, components, files, public APIs, tests, and commands.

When a note becomes an accepted architecture decision, move or summarize it into `docs/02_ADR` and link back to the original note.
