---
status: executing
complexity: complex
complexity_source: auto
development_mode: tdd
source_design: ../docs/brainstorms/2026-11-01-content-reload-notifications.md
design_revision: 11
plan_revision: 3
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
- [x] 2026-11-02: WP-01 standalone probe built without warnings/errors; all eight scenarios (including 40 acquisition/collection races) passed on Linux ext4 and tmpfs. Independent read-only probe review passed after replacing timing-only exclusion checks with observed lock-contention handshakes. Windows CI evidence remains required before WP-01 acceptance.
- [x] 2026-11-02: WP-01 accepted after Windows and Ubuntu CI jobs both passed: https://github.com/Karpiklalloly/Karpik.Engine/actions/runs/41478137701 . Root repeated the ext4 probe successfully. Draft task PR: https://github.com/Karpiklalloly/Karpik.Engine/pull/112 . WP-02 started.
- [x] 2026-11-02: Production generation process checks passed on Linux ext4: two live owners through publication and crashes, simultaneous publishers, 12 real acquisition/collection races, and a process killed while the actual build gate was held. Added a portable process host and Windows/Linux CI execution. WP-02 remains in progress pending metadata-integrity review fixes and final checks.
- [x] 2026-11-02: WP-02 local checks passed after review fixes: Content 188/188, Runtime 48/48 including existing allocation tests and physical legacy/current-generation reloads. Added modeled valid-newer-projection/old-marker recovery, unknown identity migration and read-only non-development fallback. Standalone WP-04 transport primitive passed 38/38 tests including real process delivery/death, bounded handlers and truthful Unknown results; CLI/runtime integration remains pending. Windows production/transport checks are next.
- [x] 2026-11-02: WP-02 accepted: Windows/Ubuntu production-process and transport CI passed on commit 98ebca4 (https://github.com/Karpiklalloly/Karpik.Engine/actions/runs/41482381335). Root rebuilt/reran ext4 production checks successfully. OpenGL `--content` and `--di` probes passed after the store changes; build emitted existing project warnings plus sandbox NuGet audit-network warning. All storage/lifetime review blockers fixed; modeled complete-candidate/valid-projection recovery closes its remaining test gap. WP-03 started.
- [x] 2026-11-03: Resumed after agent usage limits interrupted the previous session. Preserved unfinished coordinator/CLI/host source and resumed their focused verification; no unfinished package was accepted from inferred test results.
- [x] 2026-11-03: WP-03 verification passed: coordinator 9/9 twice, full Runtime 57/57; 100,000 warmed idle boundaries and 11 measured update publications allocated zero caller bytes. Verified 8192-change full replacement, latest pending/deep-copy/store ownership, actual-basis diff, retry/no-op, 20 stop/commit races, prompt Applied acknowledgment during pinned retirement and sticky backend failure. Independent package review is in progress.
- [x] 2026-11-03: Main accepted WP-03 after inspecting actual coordinator code, buffer/atomic/store lifetimes and test coverage. Started WP-05 host activation against its stable API. Full independent integrated review remains mandatory in WP-06.
- [x] 2026-11-03: WP-05 independent prerequisites passed focused 13/13 and Runner 154/154 using repository CI test serialization. An initial local parallel run had one existing scheduler timeout; isolated retry passed 1/1. General Windows CI on 98ebca4 failed only two Transport child-process tests because an unlisted helper built Debug while the test project built Release. Added the helper to the solution; minimal Release solution build and transport 38/38 passed. Full Windows rerun pending.
- [x] 2026-11-03: CLI portion of WP-04 accepted: final explicit build/retry suite 26/26 after parser regressions, ordinary CLI compatibility 23/23. Build and retry retain their generation through delivery wait; timeout remains Unknown without canceling application. Exit codes 0 applied/superseded, 4 known delivery failure, 5 unknown; usage/build/unexpected codes preserved. Runtime endpoint/host integration remains pending.
- [x] 2026-11-05: Resumed preserved host integration after another usage-limit interruption. Independent WP-03 read-only review found no blocking defect. General Windows CI on da010ac passed; separate Windows/Linux matrix passed after recursive dependency checkout was added (74f7f2a, https://github.com/Karpiklalloly/Karpik.Engine/actions/runs/42046714273).
- [x] 2026-11-05: Added ordinary read-only generation-marked package fallback without granting reload ownership. Observed 2 failing regression cases, then 2/2 green and full Runtime 59/59. Pure metadata/projection validation still rejects corruption, and opt-in requires IsGenerationCaptured. Host process/GPU integration verification continues.
- [x] 2026-11-05: WP-04/WP-05 accepted locally: final Core 188/188, Runtime 59/59, Runner 171/171. Host 17/17 includes four real StaticEngineHost processes (Client/Server × standalone/launcher IPC), repeated byte[] resolution, held Client Begin preserving its change buffer while the next update waits, boundary allocations, stop/default/identity cases. Dynamic/Static EngineRunner injection on both sides and actual PluginLoadContext identity/restart propagation are tested separately. No claim of a Dynamic process cross-product smoke.
- [x] 2026-11-05: Real OpenGL `--content` passed old-green/new-red transfer/retirement/shutdown; enhanced `--di` passed with an actual coordinator update and old writing-buffer pin in both registration/activation orders. Default no-op lifetime preserves generated Static consumers whose factories resolve optional constructor dependencies. Build/runtime ADRs and usage guide updated. Final independent integrated review and Windows/Linux CI remain pending.
- [x] 2026-11-05: Final independent integrated read-only review found no blocking/actionable defects. A subsequent real generated Static host build reproduced KCORE002 from auto-discovery of the framework installer; nesting that private installer in partial EngineRunner fixed the build. Generated smoke and focused host 17/17 passed after encapsulation; reviewer also cleared that exact correction. Final Windows/Linux CI is the remaining acceptance gate.
- [x] 2026-11-05: Final dedicated Windows/Linux CI passed on 2534d40 (https://github.com/Karpiklalloly/Karpik.Engine/actions/runs/42051052849). General Windows CI exposed premature test DLL deletion after collectible-context Unload; moved the DI probe into a NoInlining helper and require observed collection before cleanup. Targeted local regression passed 1/1; full CI rerun remains required.
- [ ] Implement and validate all packages, integration, independent review, and publication.

## Surprises & Discoveries

- `ContentAtomicPublisher` deletes stale artifacts and can overwrite paths; `FileContentStore` reads lazily. Existing manifest publication alone does not preserve captured bytes.
- `ContentRegistry.PrepareAsync` uses the basis store and retains unchanged slots, so collection must protect actual slot/store lifetimes as well as pending updates.
- Existing worker IPC assumes a launcher connection; standalone requires its own opted-in endpoint.
- The existing structural graph predates current snapshot changes; actual registry/source and the runtime/build ADRs were used to verify lifecycle contracts. QMD tooling is unavailable in this session.
- On Linux ext4/tmpfs, independent `FileShare.None` opens exclude each other both across processes and within one process; unique `FileShare.Read` claim handles remain independently owned and are released by process death. This does not establish behavior on untested filesystems.
- WP-02 review identified two additional safety cases: a GUID marker alone does not establish a trusted current/candidate schema for collection, and valid mutable projection/cache bytes do not prove the immutable current copy is still complete. Regression tests and fixes are required before package acceptance.

## Decision Log

- 2026-11-02: Follow design revision 11, including bounded latest-pending delivery and failure-closed generation retention. TDD automatically selected for observable behavior. Scope and cross-process risks justify complex workflow.
- 2026-11-02: WP-01 candidate protocol uses a permanent namespace gate and one unique open-handle claim per captured store/generation owner. Acquisition and collection share the gate; release closes only the owner handle. Production must prune proven-stale claims during cold operations and preserve actual slot/in-flight lifetimes. Feasibility code is isolated under `tests/ContentGenerationOwnership.Probe`; it does not change runtime behavior.
- 2026-11-05: Host composition provides an empty change source and no-op reload lifetime without starting an endpoint/worker when opt-out. Generated Static factories currently resolve optional constructor parameters, so the no-op lifetime is also required for source-compatible renderer construction.
- 2026-11-05: Read-only marked packages retain ordinary non-owning static-file startup after metadata/projection validation. They cannot enable reload; owned captured generations remain mandatory for development delivery. This extends the already accepted read-only legacy fallback without weakening opted-in ownership.

## Outcomes & Retrospective

Immutable publication/retention, bounded coordinator/transport, CLI delivery/retry and both host paths are implemented and pass local verification. Final independent review and remote CI are pending; the feature is not yet marked complete. Durable contracts are recorded in `docs/02_ADR/content-pipeline-build-contract.md`, `docs/02_ADR/content-runtime-snapshots.md` and usage in `docs/content-reload.md`.

## Context and Orientation

`src/Karpik.Content.Core/ContentBuildCoordinator.cs` and `ContentAtomicPublisher.cs` publish builds; `ContentManifestDiff.cs` compares manifests. `src/Karpik.Content.Tool/Program.cs` hosts the CLI. `src/Karpik.Content.Runtime/ContentRegistry.cs`, `FileContentStore.cs` and `IContentUpdateRegistry.cs` provide snapshots, captured stores and one outstanding update. `src/Karpik.Engine.Core.Runner/WorkerHost.cs` hosts fixed server ticks and threaded client frames, for both dynamic and static hosts. The current `docs/02_ADR/content-runtime-snapshots.md` defines quiescent publication and resource retirement.

## Real-Time Assessment

Build, IPC, artifact ownership and preparation are cold control-plane operations. Frame/tick paths only poll or exchange preallocated state and publish a bounded dense change buffer; no per-frame allocation, blocking, filesystem operations, locks, or user callbacks. Simulation observes changes at its safe boundary. Preserve fixed dt, backend ownership, and Client/Server/Shared separation. Measure allocations after warmup, including no-change and update delivery paths. Avoid storing reloadable delegates or gameplay state in static fields.

## Plan of Work and Milestones

Each package begins with meaningful failing tests before implementation, except the platform feasibility probe (normal mode); run its checks before advancing. Packages may be scheduled dynamically after approval; coordinate shared files and build outputs.

### WP-01 — Prove crash-safe generation ownership

- Status: completed. Depends on: none. Acceptance: AC4, AC7.
- Establish a repeatable cross-process probe for supported Windows/Linux filesystem and process-lifetime behavior: concurrent acquisition versus collection, process death, shared owners, and startup races. Choose a protocol that releases OS-owned claims after crash and prevents check/delete races; do not rely on PID alone. Document its filesystem assumptions and fail closed when safety is uncertain. If proof fails, stop affected implementation and revisit design rather than claim safety.
- Verification: execute automated multi-process probe on available platforms; document unavailable platform evidence and run targeted tests.

### WP-02 — Immutable publication and collection

- Status: completed. Depends on: WP-01. Acceptance: AC4, AC7.
- Extend `ContentBuildCoordinator`, `ContentAtomicPublisher`, stores and relevant contracts as required for immutable published generations and bounded, crash-safe ownership. Preserve ordinary build compatibility, incremental cache correctness, unchanged slots and in-flight loads; serialize concurrent publishers/collectors, handle failed publications and recovery. Do not modify frame loop or IPC.
- Verification: Content and Runtime targeted tests including crash, concurrent build, old unloaded/in-flight asset, retention and reclamation, recovery and unchanged content.

### WP-03 — Runtime coordinator and change delivery

- Status: completed. Depends on: WP-02. Acceptance: AC3, AC4, AC5, AC6, AC7.
- Add side-neutral coordinator for latest pending generation, supersession, cold prepare, retry/failure and completion; track generation ownership through actual slot and load lifetimes. Publish added/updated/removed IDs computed against the actual applied root through bounded preallocated simulation-facing data. Preserve explicit reference resolution, no automatic ECS mutation, and safe shutdown. No transport or direct Client dependency.
- Verification: Runtime tests for race, supersession, failure, no-op, stale references, bounded buffers, repeated updates and allocation budget.

### WP-04 — Targeted endpoint and CLI

- Status: completed. Depends on: WP-02, WP-03. Acceptance: AC1, AC2, AC3, AC7.
- Internal scheduling: the independent bounded local transport/protocol primitive may be developed and tested before WP-02/WP-03 finish. CLI generation retention and runtime/coordinator wiring remain dependent on accepted WP-02/WP-03 results. This does not accept WP-04 early or change its behavioral contracts.
- Implement opt-in local addressable endpoint usable independently of launcher IPC, validate target/namespace and message bounds; wire explicit CLI delivery after successful build and retry of an already published result. Correlate requests with applied/superseded/failed responses, limit wait time, report timeout as unknown and preserve separately reported build success. No network-facing or Editor integration.
- Verification: tool and endpoint tests for malformed/mismatched requests, unavailable process, retries, concurrent requests, supersession, shutdown, timeout and disabled default.

### WP-05 — Client and Server integration

- Status: completed. Depends on: WP-03, WP-04. Acceptance: AC1, AC5, AC6, AC7.
- Internal scheduling: independent opt-in launch-option propagation and stable Dynamic assembly identity preparation may proceed before coordinator acceptance; boundary/endpoint/lifetime integration remains gated on the accepted coordinator and transport contracts.
- Integrate `WorkerHost` and applicable shared host lifecycle so static and dynamic, launcher-managed and standalone Client/Server use safe frame/tick boundaries. Preserve GPU lifetime and shutdown sequencing. No work on frame path beyond bounded nonblocking publication.
- Verification: runner/headless integration tests for both sides, fixed-tick ordering, concurrent client simulation and rendering, shutdown, repeated reload and allocation checks; actual graphics smoke where available.

### WP-06 — Integration, docs and independent review

- Status: in_progress. Depends on: WP-02, WP-03, WP-04, WP-05. Acceptance: AC1–AC7.
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

- 2026-11-02 (plan revision 2, approved baseline remains revision 1/design 11): separate the WP-04 transport primitive from coordinator/CLI integration so its message validation, endpoint identity and bounded connection tests can proceed independently of WP-02. No requirements, architecture, ACs or public behavior change; runtime/CLI integration remains gated on WP-02/WP-03 acceptance.
- 2026-11-02 (plan revision 3): similarly schedule WP-05 launch propagation and stable Content assembly sharing independently. New Runner references require default-context Content identities in Dynamic plugin loading; discovery verified this prerequisite from source. Endpoint activation and frame integration remain dependent on accepted WP-03/WP-04. Requirements and ACs remain unchanged.
