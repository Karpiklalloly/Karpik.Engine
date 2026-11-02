---
status: executing
complexity: complex
complexity_source: auto
development_mode: tdd
source_design: ../docs/brainstorms/2026-11-01-content-reload-notifications.md
design_revision: 11
plan_revision: 1
approved_plan_revision: 1
approved_design_revision: 11
---
# Deliver targeted Content reload notifications

This living ExecPlan follows `plans/PLANS.md`. The source design defines the agreed scope and acceptance criteria. Plan revision 1 and design revision 11 were approved on 2026-11-02. The developer deferred implementation initially and explicitly requested execution on 2026-11-02.

## Purpose / Big Picture

An explicit successful Content build targets one opted-in runtime instance, including standalone and launcher-managed Client/Server. It waits for the application outcome while preserving a separate successful-build result. Immutable generations survive concurrent updates and are automatically collected when safe. Game systems receive changes and explicitly re-resolve references.

## Progress

- [x] 2026-11-02: Design and implementation plan prepared and approved; implementation explicitly deferred by the developer.
- [x] 2026-11-02: Execution requested; resumed the approved task branch and started WP-01 cross-process ownership feasibility probe.
- [x] 2026-11-02: Baseline checks passed: Content 176/176, Content Runtime 43/43, Runner 141/141. Used the three targeted `dotnet test` commands with `-m:1 -nr:false --no-restore`; Content was rerun with `--no-build` outside sandbox after VSTest socket creation was denied. Existing compiler/analyzer warnings remain.
- [ ] Implement and validate all packages, integration, independent review, and publication.

## Surprises & Discoveries

- `ContentAtomicPublisher` deletes stale artifacts and can overwrite paths; `FileContentStore` reads lazily. Existing manifest publication alone does not preserve captured bytes.
- `ContentRegistry.PrepareAsync` uses the basis store and retains unchanged slots, so collection must protect actual slot/store lifetimes as well as pending updates.
- Existing worker IPC assumes a launcher connection; standalone requires its own opted-in endpoint.

## Decision Log

- 2026-11-02: Follow design revision 11, including bounded latest-pending delivery and failure-closed generation retention. TDD automatically selected for observable behavior. Scope and cross-process risks justify complex workflow.

## Outcomes & Retrospective

Not implemented. Record verification and link the updated ADR on completion.

## Context and Orientation

`src/Karpik.Content.Core/ContentBuildCoordinator.cs` and `ContentAtomicPublisher.cs` publish builds; `ContentManifestDiff.cs` compares manifests. `src/Karpik.Content.Tool/Program.cs` hosts the CLI. `src/Karpik.Content.Runtime/ContentRegistry.cs`, `FileContentStore.cs` and `IContentUpdateRegistry.cs` provide snapshots, captured stores and one outstanding update. `src/Karpik.Engine.Core.Runner/WorkerHost.cs` hosts fixed server ticks and threaded client frames, for both dynamic and static hosts. The current `docs/02_ADR/content-runtime-snapshots.md` defines quiescent publication and resource retirement.

## Real-Time Assessment

Build, IPC, artifact ownership and preparation are cold control-plane operations. Frame/tick paths only poll or exchange preallocated state and publish a bounded dense change buffer; no per-frame allocation, blocking, filesystem operations, locks, or user callbacks. Simulation observes changes at its safe boundary. Preserve fixed dt, backend ownership, and Client/Server/Shared separation. Measure allocations after warmup, including no-change and update delivery paths. Avoid storing reloadable delegates or gameplay state in static fields.

## Plan of Work and Milestones

Each package begins with meaningful failing tests before implementation, except the platform feasibility probe (normal mode); run its checks before advancing. Packages may be scheduled dynamically after approval; coordinate shared files and build outputs.

### WP-01 — Prove crash-safe generation ownership

- Status: in_progress. Depends on: none. Acceptance: AC4, AC7.
- Establish a repeatable cross-process probe for supported Windows/Linux filesystem and process-lifetime behavior: concurrent acquisition versus collection, process death, shared owners, and startup races. Choose a protocol that releases OS-owned claims after crash and prevents check/delete races; do not rely on PID alone. Document its filesystem assumptions and fail closed when safety is uncertain. If proof fails, stop affected implementation and revisit design rather than claim safety.
- Verification: execute automated multi-process probe on available platforms; document unavailable platform evidence and run targeted tests.

### WP-02 — Immutable publication and collection

- Status: pending. Depends on: WP-01. Acceptance: AC4, AC7.
- Extend `ContentBuildCoordinator`, `ContentAtomicPublisher`, stores and relevant contracts as required for immutable published generations and bounded, crash-safe ownership. Preserve ordinary build compatibility, incremental cache correctness, unchanged slots and in-flight loads; serialize concurrent publishers/collectors, handle failed publications and recovery. Do not modify frame loop or IPC.
- Verification: Content and Runtime targeted tests including crash, concurrent build, old unloaded/in-flight asset, retention and reclamation, recovery and unchanged content.

### WP-03 — Runtime coordinator and change delivery

- Status: pending. Depends on: WP-02. Acceptance: AC3, AC4, AC5, AC6, AC7.
- Add side-neutral coordinator for latest pending generation, supersession, cold prepare, retry/failure and completion; track generation ownership through actual slot and load lifetimes. Publish added/updated/removed IDs computed against the actual applied root through bounded preallocated simulation-facing data. Preserve explicit reference resolution, no automatic ECS mutation, and safe shutdown. No transport or direct Client dependency.
- Verification: Runtime tests for race, supersession, failure, no-op, stale references, bounded buffers, repeated updates and allocation budget.

### WP-04 — Targeted endpoint and CLI

- Status: pending. Depends on: WP-02, WP-03. Acceptance: AC1, AC2, AC3, AC7.
- Implement opt-in local addressable endpoint usable independently of launcher IPC, validate target/namespace and message bounds; wire explicit CLI delivery after successful build and retry of an already published result. Correlate requests with applied/superseded/failed responses, limit wait time, report timeout as unknown and preserve separately reported build success. No network-facing or Editor integration.
- Verification: tool and endpoint tests for malformed/mismatched requests, unavailable process, retries, concurrent requests, supersession, shutdown, timeout and disabled default.

### WP-05 — Client and Server integration

- Status: pending. Depends on: WP-03, WP-04. Acceptance: AC1, AC5, AC6, AC7.
- Integrate `WorkerHost` and applicable shared host lifecycle so static and dynamic, launcher-managed and standalone Client/Server use safe frame/tick boundaries. Preserve GPU lifetime and shutdown sequencing. No work on frame path beyond bounded nonblocking publication.
- Verification: runner/headless integration tests for both sides, fixed-tick ordering, concurrent client simulation and rendering, shutdown, repeated reload and allocation checks; actual graphics smoke where available.

### WP-06 — Integration, docs and independent review

- Status: pending. Depends on: WP-02, WP-03, WP-04, WP-05. Acceptance: AC1–AC7.
- Verify end-to-end workflows and update relevant build/runtime ADRs and usage docs. Independently review integrated diff with a read-only review subagent, fix findings and re-run affected checks. Update this plan with actual evidence and compatibility impact.
- Verification: execute commands below, end-to-end launcher-managed and standalone Client/Server runs, failure and repeated-update cases. GPU and Windows checks require appropriate hosts; do not claim unexecuted checks.

## Concrete Steps

From repository root, use test-first targeted filters while developing, then run sequentially:

    dotnet test tests/Karpik.Content.Tests/Karpik.Content.Tests.csproj -m:1 -nr:false
    dotnet test tests/Karpik.Content.Runtime.Tests/Karpik.Content.Runtime.Tests.csproj -m:1 -nr:false
    dotnet test tests/Karpik.Engine.Core.Runner.Tests/Karpik.Engine.Core.Runner.Tests.csproj -m:1 -nr:false
    git diff --check

Confirm actual project paths before running; add the smallest relevant graphics and process integration projects discovered during implementation. Record command results and platform constraints in Progress.

## Validation and Acceptance

All AC1–AC7 require direct evidence, including simultaneous builds, supersession, timeout unknown, runtime death, two owners, collection races, unchanged unloaded assets, frame allocation, and both host modes on both sides. Validate build and delivery results independently. End-to-end real GPU texture replacement must retain old and new resources correctly; reuse the existing opt-in GPU audit when hardware permits.

## Idempotence and Recovery

Never collect a generation when ownership cannot be established. Failed publication preserves the last usable generation; interrupted cleanup is retryable and cannot affect a live owner. Retrying delivery is safe and produces an accurate result. Preserve existing user data and do not delete legacy output as an unverified migration step. If platform lock or filesystem semantics cannot be proven, leave cleanup disabled/fail closed and stop the acceptance gate for further decision.

## Artifacts and Notes

Source design: `docs/brainstorms/2026-11-01-content-reload-notifications.md` revision 11. Update `docs/02_ADR/content-runtime-snapshots.md` and the build contract after verified implementation. PR title, labels, compatibility notes and verification are required by repository instructions.

## Adjustments

None.
