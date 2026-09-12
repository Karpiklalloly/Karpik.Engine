# Audit the complete KarpikEngine repository

This ExecPlan is a living document maintained according to `plans/PLANS.md`.

## Purpose / Big Picture

Audit the current working tree for correctness, memory safety, concurrency, lifecycle, build, and real-time defects. The user requested a full audit after a preliminary review found four reproducible Tween defects. Deliver a prioritized report with evidence, reproduction conditions, exact source locations, and explicit coverage limitations. Do not fix production code.

## Progress

- [x] (2026-09-06) Read repository rules, inventory solution/projects, preserve existing Content modifications.
- [x] (2026-09-06) Establish CBM project and coverage; dispatch independent runtime, native, and module reviews.
- [x] (2026-09-07) Attempt solution build and all 19 first-party test projects; record build Exec failure, two compilation-blocked suites and incomplete Packager run. Sixteen suites completed successfully.
- [x] (2026-09-07) Review Content, SDK, tooling, project model, editor, launcher, configurator, and build scripts with bounded coverage recorded in the report.
- [x] (2026-09-07) Consolidate 34 findings, distinguish source proofs from executable reproductions, and record coverage and limitations.

## Surprises & Discoveries

- CBM generation is `2026-09-06T07:58:15Z`; many file metadata stamps changed. Current source reads are required. Jobs and some ECS/physics files contain recorded parse gaps.
- Existing Content and documentation-related changes belong to the user and must remain untouched.
- Preliminary checks passed 90 tests across Content, Content.Runtime, and Network.Codegen; these do not cover the reproduced Tween defects.
- Final graph generation observed: `2026-09-06T11:34:36Z`; current-source fallback used for changed metadata and parser gaps.
- Core.Runner initially failed 20 named-pipe/lifecycle tests inside the sandbox; rerunning the complete ready suite outside it passed 128/128. The initial result is superseded.
- Completed successful suites contain 727 passed and 18 skipped tests. Packager completed only 4 passed/2 failed before its hang timeout; StaticAnalyzer and Input could not compile in the attempted runs.
- Full solution build still fails in Directory.Build.targets Exec with dotnet exit 9009, while direct Configurator --validate passes. No claim of a green full build.

## Decision Log

- Decision: Audit current working tree, not only committed changes. Rationale: user requested project-wide errors. Date: 2026-09-06.
- Decision: Separate read-only subsystem audits and serialize repository builds/tests. Rationale: independent reviews benefit from parallel work; shared build outputs do not. Date: 2026-09-06.
- Decision: Treat vendored DragonECS through engine integration, builds, and relevant source paths; do not claim exhaustive upstream-library or dependency security certification. Date: 2026-09-06.

## Outcomes & Retrospective

Audit report delivered in `plans/full-audit-report.md`: 10 P1 and 24 P2 findings, subsystem coverage, reproduction conditions, repair directions, test outcomes and unresolved verification limits. No production or test code changed. Existing user edits preserved. This completes the requested audit pass, not exhaustive proof of correctness or implementation of fixes.

## Context and Orientation

Runtime: `Karpik.Engine.Core`, `Karpik.Engine.Core.Runner`, core generator, ECS extensions. Native execution: `first-parties/Karpik.Jobs`. Game modules: `Modules`, network/stat generators. Content: `Karpik.Content.*`. Developer tooling: `Karpik.Engine.ProjectModel`, `Karpik.Engine.Sdk*`, `Karpik.Engine.Tooling`, `Karpik.Engine.Packager`, `Karpik.Editor`, `Karpik.Launcher`, `Configurator`, `Tools/StaticAnalyzer`, templates and build scripts. The graph is a discovery aid, not proof of runtime correctness.

## Real-Time Assessment

No runtime edits. Inspect frame/fixed tick allocations, fixed dt, ECS layout, concurrency, disposal, native ownership, network validation and delivery. Report demonstrable failures or measurable contract violations rather than generic optimization preferences. No GPU/platform behavior claims without execution evidence.

## Plan of Work

Create audit logs under `.tmp/full-audit`. Build the solution with one MSBuild node and node reuse disabled. Discover test project declarations and execute them sequentially, recording exit status and test summaries. Follow discovered symbols through graph relationships and current source, consulting local contracts where relevant. Each reviewer records bounded coverage and evidence. Merge results into an audit report and update this plan.

## Milestones

1. Inventory and baseline: solution build and per-project test result log.
2. Subsystem audits: exact source evidence and minimal isolated reproductions where practical.
3. Report: deduplicate findings, rank severity, distinguish environmental failures, identify untested platform paths.

## Concrete Steps

Working directory: repository root.

- `dotnet build KarpikEngine.slnx --no-restore -m:1 -nr:false --verbosity quiet`
- `dotnet test <test-project.csproj> --no-restore -m:1 -nr:false --verbosity quiet`
- Read `.tmp/full-audit/*.log` and subsystem reports; rerun failing cases only when their cause requires confirmation.

Expected observations: successful compilation and passing tests, or precise failures recorded with source/environment attribution. Integration tests may require external SDK/platform prerequisites; record actual failures rather than silently skipping them.

## Validation and Acceptance

Every first-party production area has a review disposition. Every discovered test project has an executed outcome or explicit reason it could not run. Confirmed findings identify trigger, consequence, path/line and supporting evidence. No promise of absence of bugs follows from passing tests or clean graph coverage.

## Idempotence and Recovery

Read-only review is repeatable. Builds change generated outputs only. Isolated repros belong in temporary directories. Never reset existing changes, delete user files, or change runtime behavior. Stop only audit-owned timed-out processes.

## Artifacts and Notes

- `.tmp/full-audit/`: build/test logs, subsystem review evidence, isolated check artifacts.
- `plans/full-audit-report.md`: durable consolidated report in Russian.
