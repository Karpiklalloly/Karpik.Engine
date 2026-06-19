# Build the generated ECS update scheduler

This ExecPlan is a living document. It must be maintained according to `plans/PLANS.md`.

## Purpose / Big Picture

Replace the isolated Dragon `IEcsRunParallel` prototype with a public Karpik scheduler for `ISystemUpdate`. Updates are parallel by default, statically validated at build time, and executed through compact generated metadata with no runtime reflection in the frame path.

`ISystemFixedUpdate` remains sequential in `0.5`.

## Progress

- [x] (2026-06-03 00:33 +04:00) Design agreed as child plan 3 of `plans/scheduler-jobs-memory-execplan.md`.
- [x] (2026-06-05 23:07 +04:00) Preserved prototype graph behavior as baseline evidence: existing dependency graph tests still pass, and `ECS.Core.Tests/PrototypeDependencyGraphBaselineTests.cs` now covers read/read overlap, mixed read/write aspect conflicts, unknown-access barrier behavior, and `SystemExecutionNode.Destroy` cleanup.
- [x] (2026-06-06 11:57 +04:00) Added scheduler metadata attributes and diagnostics: `SequentialSystemAttribute`, access/order attributes, scheduler diagnostic IDs K003-K008, analyzer release tracking, and descriptor tests.
- [x] (2026-06-06 12:09 +04:00) Implemented first Roslyn analysis slice for `ISystemUpdate.Update()`: direct `EcsPool<T>` write access, `EcsReadonlyPool<T>` read access, source helper traversal, helper summary validation, delegate/dynamic opaque access, sequential opt-out, and managed-component summary rejection.
- [x] (2026-06-06 12:25 +04:00) Hardened library-boundary calls: metadata-only methods without `[Reads<T>]` / `[Writes<T>]` now report opaque access, while summarized external helpers and reviewed BCL math calls remain accepted.
- [x] (2026-06-06 12:53 +04:00) Hardened unresolved dispatch: virtual/interface calls without scheduler summaries now report opaque access, while summarized interface calls are accepted as explicit contracts.
- [x] (2026-06-06 12:58 +04:00) Added `[MainThreadOnly]` marker and K006 analyzer enforcement for method/type calls from `ISystemUpdate.Update()`.
- [x] (2026-06-06 13:02 +04:00) Hardened reflection checks: `typeof(...)` inside scheduled `ISystemUpdate.Update()` now reports opaque access.
- [x] (2026-06-06 13:07 +04:00) Added first reviewed world facade summaries: Dragon `EcsWorld.GetPool<T>()` / `GetPoolUnchecked<T>()` and wrapper `World.Get<T>()` are inferred as write access instead of opaque calls.
- [x] (2026-06-06 13:18 +04:00) Extended reviewed wrapper facade summaries: `World.Has<T>()` and `World.TryGet<T>()` infer read access; `World.Add<T>()`, `World.Set<T>()`, `World.Del<T>()`, and `World.Event<T>()` infer write access.
- [x] (2026-06-06 14:06 +04:00) Hardened generic source-helper traversal: constructed generic helper calls now substitute type arguments before recording inferred ECS access.
- [x] (2026-06-06 14:17 +04:00) Added conservative aspect inference for `Where<TAspect>()`: `EcsReadonlyPool<T>` fields infer read access and `EcsPool<T>` fields infer write access.
- [x] (2026-06-06 14:29 +04:00) Hardened unsafe aliasing checks: address-of operations inside scheduled `ISystemUpdate.Update()` now report opaque access.
- [x] (2026-06-06 14:33 +04:00) Classified wrapper lifecycle facade calls (`Enable`, `AddEnabled`, `Disable`, `DelEnabled`, including async variants) as opaque scheduled-update access.
- [x] (2026-06-06 17:03 +04:00) Added representative migrated-system coverage for input/RPC-style update systems: unsummarized external input/RPC calls fail closed, while `[SequentialSystem]` opt-out accepts the same body.
- [x] (2026-06-06 17:10 +04:00) Added first generated ECS update registry provider slice: codegen emits per-assembly update system descriptors from explicit scheduler metadata attributes, backed by runtime descriptor contracts.
- [x] (2026-06-06 17:20 +04:00) Added startup-only `EcsUpdateGraphBuilder`: registered update system types are validated against generated descriptors, component/system types are flattened to dense IDs, conflict/order/sequential dependencies are emitted as compact arrays, and cycles fail initialization.
- [x] (2026-06-06 17:22 +04:00) Validated latest scheduler metadata/codegen/graph slices: `dotnet test ECS.Core.Tests\ECS.Core.Tests.csproj -m:1 -nr:false --no-restore` passed 34/34, `dotnet build Modules\Shared\ECS\ECS.Core\ECS.Core.csproj -m:1 -nr:false --no-restore` passed with 0 warnings, and `dotnet test Tools\StaticAnalyzer.Tests\StaticAnalyzer.Tests.csproj -m:1 -nr:false --no-restore` passed 36/36 with only the existing `NU1900` vulnerability-feed warning from unavailable NuGet network access.
- [x] (2026-06-06 17:45 +04:00) Moved scheduler metadata contracts, generated registry contracts, and compact graph builder to `Karpik.Engine.Core/Scheduling` so `Karpik.Engine.Core.Runner` can consume generated descriptors without creating a `Runner -> ECS.Core -> Runner` project cycle.
- [x] (2026-06-06 17:45 +04:00) Integrated `ISystemUpdate` runtime scheduling: Dragon `UpdateSystem` wrappers preserve existing layer/order and DI, `EngineRunner` builds the generated graph at startup, production mode schedules unmanaged value jobs through `JobScheduler`, and deterministic/single-thread modes are selectable through `EngineRunner.UpdateSchedulerMode`.
- [x] (2026-06-06 17:45 +04:00) Migrated current MyGame `ISystemUpdate` implementations by marking opaque ImGui/input/network/physics/lifecycle-facade systems `[SequentialSystem]`; the Dragon `IEcsRunParallel` prototype remains quarantined by `DoNotUseDragonLifecycleAnalyzer` outside `ECS.Core`.
- [x] (2026-06-06 17:45 +04:00) Updated server fixed-loop overload handling: bounded catch-up diagnostics remain, but pending fixed tick backlog is preserved instead of resetting `nextTickTime`.
- [x] (2026-06-06 17:45 +04:00) Ran scheduler acceptance gate: `dotnet test ECS.Core.Tests\ECS.Core.Tests.csproj -m:1 -nr:false --no-restore` passed 34/34; `dotnet test Karpik.Engine.Core.Runner.Tests\Karpik.Engine.Core.Runner.Tests.csproj -m:1 -nr:false --no-restore` passed 8/8 including parallel overlap, dependency serialization, deterministic order, and 0 B calling-thread allocation after warm-up; `dotnet test Tools\StaticAnalyzer.Tests\StaticAnalyzer.Tests.csproj -m:1 -nr:false --no-restore` passed 36/36 with existing `NU1900`; `dotnet build ServerLauncher\ServerLauncher.csproj -m:1 -nr:false --no-restore` passed; `dotnet build ClientLauncher\ClientLauncher.csproj -m:1 -nr:false --no-restore` passed.

## Surprises & Discoveries

- Observation: Prototype graph construction reflects nested `EcsAspect` definitions into `HashSet<Type>` and `List<SystemExecutionNode>`.
  Evidence: `Modules/Shared/ECS/ECS.Core/SystemExecutionNode.cs`.

- Observation: Prototype conflict evaluation allocates a temporary `HashSet<Type>` for each compared pair.
  Evidence: `Modules/Shared/ECS/ECS.Core/IEcsRunParallel.cs`.

- Observation: The prototype treats unknown access as a barrier relative to registration order, but later disjoint systems do not depend on each other unless they conflict or cross the unknown node.
  Evidence: `PrototypeDependencyGraphBaselineTests.DependencyGraph_UnknownAccess_BarriersAllLaterSystemsInRegistrationOrder`.

- Observation: `Builder` currently wraps every `ISystemUpdate` as a sequential Dragon `UpdateSystem`.
  Evidence: `Karpik.Engine.Core.Runner/Builder.cs`.

- Observation: The repository already has both a Roslyn analyzer project and an incremental generator project, but neither currently generates ECS access metadata.
  Evidence: `Tools/StaticAnalyzer` and `Karpik.Engine.Core.Generator/Karpik.Engine.Core.Codegen`.

- Observation: Scheduler metadata is declarative only; the next milestone must still infer, validate, and emit diagnostics for actual `ISystemUpdate.Update()` bodies.
  Evidence: `SchedulerMetadataAttributeTests` validates attribute shape, and `SchedulerDiagnosticDescriptorTests` validates descriptor IDs/severity, but no analyzer traversal exists yet.

- Observation: Source helper summaries cannot be trusted as boundaries when the helper body is available; the analyzer must still traverse the source body and reject contradictory summaries.
  Evidence: `EcsUpdateSchedulerAnalyzerTests.SourceHelperSummaryContradictingBody_IsRejected`.

- Observation: Metadata-only helper bodies are invisible to the analyzer, so accepting them without summaries would silently hide ECS access behind a library boundary.
  Evidence: `EcsUpdateSchedulerAnalyzerTests.ExternalMethodWithoutSummary_IsOpaque`.

- Observation: Source virtual methods are not safe to analyze as concrete helper bodies because runtime dispatch may reach an override that is not visible from the call site.
  Evidence: `EcsUpdateSchedulerAnalyzerTests.VirtualSourceMethodWithoutSummary_IsOpaque`.

- Observation: Main-thread-only API must be detected before source-helper traversal or summary handling, otherwise a marked empty helper body could be incorrectly treated as scheduler-safe.
  Evidence: `EcsUpdateSchedulerAnalyzerTests.MainThreadOnlyMethodCallFromUpdate_IsRejected`.

- Observation: Reflection can enter the operation tree without an invocation, so external-call fail-closed logic does not catch `typeof(...)`.
  Evidence: `EcsUpdateSchedulerAnalyzerTests.TypeOfReflectionInsideUpdate_IsOpaque`.

- Observation: Dragon world pool access and Karpik world wrapper access arrive as metadata calls in analyzer tests, so they need reviewed summaries before external-call fail-closed logic.
  Evidence: `EcsUpdateSchedulerAnalyzerTests.DragonWorldGetPool_IsInferredAsWrite` and `EcsUpdateSchedulerAnalyzerTests.WrappedDefaultWorldGet_IsInferredAsWrite`.

- Observation: Wrapper facade methods are also metadata calls in analyzer tests, so unreviewed `World.Has<T>()`, `World.TryGet<T>()`, `World.Add<T>()`, `World.Set<T>()`, `World.Del<T>()`, and `World.Event<T>()` fail closed as opaque access.
  Evidence: red run of `Tools\StaticAnalyzer.Tests\StaticAnalyzer.Tests.csproj` before extending `TryGetWorldFacadeAccess`, where the new wrapper tests reported `K003`.

- Observation: Source generic helper traversal originally recorded the type parameter `T`, not the constructed component type from `WriteGeneric<Position>()`, so a write through `EcsPool<T>` did not contradict `[Reads<Position>]`.
  Evidence: red run of `EcsUpdateSchedulerAnalyzerTests.GenericSourceHelperSubstitutesComponentTypeArguments`, where diagnostics were empty before type substitution was added.

- Observation: Real ECS systems commonly express access through nested `EcsAspect` classes and `Where(out Aspect)`, so treating `Where` as opaque blocks migration even when aspect fields are statically visible.
  Evidence: `Modules/Shared/Physics/Physics2D.Core/ECS/Physics2DBodyRestoreSystem.cs` and red runs of `WrappedDefaultWorldWhereReadonlyAspect_IsInferredAsRead` / `WrappedDefaultWorldWhereMutableAspect_IsInferredAsWrite`.

- Observation: Unsafe address-of operations enter the operation tree without a method invocation, so external-call fail-closed logic does not catch pointer aliasing paths.
  Evidence: red run of `EcsUpdateSchedulerAnalyzerTests.UnsafeAddressOfInsideUpdate_IsOpaque`, where diagnostics were empty before `VisitAddressOf` was added.

- Observation: Wrapper lifecycle facade methods run arbitrary component lifecycle code and some sync variants block on async handles, so treating them as ordinary write summaries would make scheduled updates look safer than they are.
  Evidence: `DragonExtensions/World.cs` lifecycle methods and red runs of `WrappedDefaultWorldLifecycleMethods_AreOpaque`, where diagnostics initially lacked the explicit lifecycle-facade reason.

- Observation: Existing client/server `ISystemUpdate` implementations include input, RPC, networking, graphics/camera state, lists, dictionaries, and other side-effect-heavy APIs that should not silently enter the parallel scheduler.
  Evidence: `MyGame/Client/MyGame.Client.Main/Systems/InputSystem.cs`, `MyGame/Client/MyGame.Client.Main/Systems/DisplaySystem.cs`, `MyGame/Server/MyGame.Server.Main/Systems/NetworkSystem.cs`, and `EcsUpdateSchedulerAnalyzerTests.MigratedInputRpcUpdateWithoutSequentialSystem_IsOpaque`.

- Observation: The analyzer's inferred access state is currently private to `EcsUpdateSchedulerAnalyzer`, so the first registry generator slice only consumes explicit class-level scheduler metadata attributes.
  Evidence: `EcsUpdateRegistryGeneratorTests.GeneratedProvider_EmitsSystemAccessOrderAndSequentialMetadata` validates explicit metadata generation, while `Tools/StaticAnalyzer/ECS/003.UpdateScheduler/EcsUpdateSchedulerAnalyzer.cs` keeps body inference inside the analyzer implementation.

- Observation: The new graph builder can preserve prototype conflict behavior without prototype reflection state in the execution graph.
  Evidence: `EcsUpdateGraphBuilderTests.Build_WriteThenRead_CreatesDependency`, `Build_ReadThenRead_DoesNotCreateDependency`, and `Build_SequentialSystem_BarriersLaterDisjointSystems`.

- Observation: Keeping scheduler contracts in `ECS.Core` would force `Karpik.Engine.Core.Runner` to reference a project that already depends on the runner through `KarpikModuleDependency`, creating an invalid cycle.
  Evidence: `Modules/Shared/ECS/ECS.Core/ECS.Core.csproj` depends on `Karpik.Engine.Core.Runner`, while runtime scheduling is wired in `Karpik.Engine.Core.Runner/Runner.cs`.

- Observation: `ISystemUpdate` wrappers can stay in the Dragon pipeline as ordering and DI adapters while runtime execution is redirected to the generated scheduler.
  Evidence: `DragonExtensions/LifeCycleBridge.cs` exposes `UpdateSystem.System`, and `Karpik.Engine.Core.Runner/Runner.cs` extracts sorted wrappers from `EcsUpdateRunner.Process` before calling `EcsUpdateScheduler.Update()`.

- Observation: The current MyGame update systems are deliberately opaque and unsuitable for default parallel execution.
  Evidence: client systems use ImGui, input, asset handles, graphics camera state, reflection, lifecycle facade calls, and RPC; server systems use physics collision buffers, network managers, lists, dictionaries, queues, and RPC senders.

## Decision Log

- Decision: Schedule only `ISystemUpdate` in `0.5`; keep `ISystemFixedUpdate`, `Begin`, `LateUpdate`, and `Render` sequential.
  Rationale: This creates a narrow, testable first public parallel contract without changing fixed-simulation semantics.
  Date/Author: 2026-06-03 / developer and agent

- Decision: Every `ISystemUpdate` is parallel-scheduled unless explicitly `[SequentialSystem]`.
  Rationale: New systems cannot silently bypass scheduling or safety checks.
  Date/Author: 2026-06-03 / developer and agent

- Decision: Unknown or contradictory ECS access fails the build.
  Rationale: Runtime sequential fallback would hide analyzer gaps and allow unsafe parallel execution assumptions to drift.
  Date/Author: 2026-06-03 / developer and agent

- Decision: Helper methods may declare `[Reads<T>]` and `[Writes<T>]` summaries; deliberately opaque update systems must use `[SequentialSystem]`.
  Rationale: Whole-program proof for arbitrary C# is not feasible. Explicit summaries keep the analyzable subset practical and fail closed.
  Date/Author: 2026-06-03 / developer and agent

- Decision: Deterministic mode executes the generated graph sequentially in stable topological order.
  Rationale: Replay and diagnostics need a stable control trajectory, not a misleading claim that worker interleaving is deterministic.
  Date/Author: 2026-06-03 / developer and agent

- Decision: Scheduler diagnostics occupy K003-K008 and remain build errors from their first public definition.
  Rationale: The scheduler contract must fail closed instead of allowing unsafe parallel execution when access or order cannot be proven.
  Date/Author: 2026-06-06 / agent

- Decision: The first analyzer slice treats `EcsPool<T>` receiver calls as write access and `EcsReadonlyPool<T>` receiver calls as read access.
  Rationale: This is conservative for scheduler safety and matches Dragon ECS mutability semantics; precise method-level read/write refinement can only reduce serialization later.
  Date/Author: 2026-06-06 / agent

- Decision: External metadata-only calls from `ISystemUpdate.Update()` fail closed unless they carry scheduler access summaries or match a reviewed pure allowlist.
  Rationale: The scheduler must not infer safety from an unavailable method body; explicit summaries make library-boundary ECS access visible to code generation.
  Date/Author: 2026-06-06 / agent

- Decision: Virtual and interface dispatch require explicit scheduler summaries unless the dispatch target is statically sealed.
  Rationale: The analyzer cannot prove which override will execute, so unsummarized dynamic dispatch is unsafe for parallel scheduling.
  Date/Author: 2026-06-06 / agent

- Decision: `[MainThreadOnly]` may mark either a method or a type, and any call to it from scheduled `ISystemUpdate.Update()` reports K006.
  Rationale: Update scheduling must not move graphics or thread-affine work onto worker threads; those operations belong in sequential lifecycle phases.
  Date/Author: 2026-06-06 / agent

- Decision: Reflection metadata access inside scheduled update code reports K003 unless it is moved into generated/startup metadata construction.
  Rationale: Runtime reflection obscures access analysis and is not suitable for frame-path scheduler decisions.
  Date/Author: 2026-06-06 / agent

- Decision: `EcsWorld.GetPool<T>()`, `EcsWorld.GetPoolUnchecked<T>()`, and wrapper `World.Get<T>()` are classified as write access in scheduler analysis.
  Rationale: These APIs expose mutable component storage or mutable refs; conservative write classification preserves scheduler safety.
  Date/Author: 2026-06-06 / agent

- Decision: Reviewed wrapper `World.Has<T>()` and `World.TryGet<T>()` are classified as read access, while `World.Add<T>()`, `World.Set<T>()`, `World.Del<T>()`, and `World.Event<T>()` are classified as write access.
  Rationale: This matches the facade semantics and avoids false opaque diagnostics during migration while keeping mutable/ref-return and event creation paths serialized conservatively.
  Date/Author: 2026-06-06 / agent

- Decision: Source generic helper analysis carries a scoped type-substitution map for method and containing-type generic arguments.
  Rationale: Scheduler metadata must be expressed in concrete component types; leaving inferred access as `T` can hide real read/write conflicts after generic helper construction.
  Date/Author: 2026-06-06 / agent

- Decision: Aspect inference is conservative: every `EcsPool<T>` field in an aspect is write access, and every `EcsReadonlyPool<T>` field is read access.
  Rationale: This may serialize more systems than a flow-sensitive per-field use analysis, but it is safe for the first scheduler and matches the existing prototype's conservative pool-type semantics.
  Date/Author: 2026-06-06 / agent

- Decision: Unsafe address-of inside scheduled update code reports K003.
  Rationale: The scheduler cannot prove pointer aliasing safety from arbitrary unsafe address paths; systems that require unsafe code must be isolated behind explicit safe contracts or marked sequential.
  Date/Author: 2026-06-06 / agent

- Decision: Wrapper lifecycle facade methods report K003 from scheduled `ISystemUpdate.Update()`.
  Rationale: These methods execute arbitrary lifecycle code and can block or mutate through passed pools; they should run in explicit lifecycle phases or behind a reviewed contract, not be inferred as scheduler-safe read/write access.
  Date/Author: 2026-06-06 / agent

- Decision: Side-effect-heavy migrated update systems must use `[SequentialSystem]` until their external APIs are reviewed and summarized.
  Rationale: Input/RPC/network/graphics calls are not ECS component access and cannot be inferred as safe parallel work from Roslyn source traversal alone.
  Date/Author: 2026-06-06 / agent

- Decision: The first generated provider uses `Type` descriptors at startup only; runtime graph flattening will convert them to stable integer IDs before frame execution.
  Rationale: This avoids reflection and metadata traversal in the frame path while keeping the first generator slice simple and verifiable.
  Date/Author: 2026-06-06 / agent

- Decision: Reusing body-inferred access between analyzer and generator is deferred; in `0.5` the first generator slice uses explicit attributes until a shared inference model is extracted.
  Rationale: The generator can ship a verifiable metadata contract now, while shared inference extraction is a separate refactor with higher analyzer regression risk.
  Date/Author: 2026-06-06 / agent

- Decision: The runtime update graph stores dependency edges as dense system ID arrays and sorted read/write component ID arrays; `Type` metadata is retained only beside the graph for startup validation and diagnostics.
  Rationale: The future frame runner can traverse integer arrays and preallocated job-handle buffers without dictionary, reflection, LINQ, or metadata access in `Update`.
  Date/Author: 2026-06-06 / agent

- Decision: Scheduler metadata contracts and graph construction live in `Karpik.Engine.Core/Scheduling` under the existing `Karpik.Engine.Shared.ECS.Scheduling` namespace.
  Rationale: `ISystemUpdate`, runner integration, and `Karpik.Jobs` are Core/Runner concerns; keeping these contracts in Core avoids a project cycle while preserving source compatibility for existing `using Karpik.Engine.Shared.ECS.Scheduling` code.
  Date/Author: 2026-06-06 / agent

- Decision: The runner preserves Dragon ordering and DI by keeping `UpdateSystem` wrappers in `EcsUpdateRunner`, but does not execute `_updateRunner.Update()` in the frame loop.
  Rationale: This keeps established `layer/order` behavior and injection behavior while moving `ISystemUpdate.Update()` execution to the generated graph scheduler.
  Date/Author: 2026-06-06 / agent

- Decision: Production parallel mode uses `JobScheduler` value jobs with an unmanaged payload containing the startup `GCHandle` pointer and dense system ID.
  Rationale: Direct delegates or the legacy `JobSystem` would allocate in the frame path; the value-job payload keeps scheduling at 0 B on the calling thread after warm-up.
  Date/Author: 2026-06-06 / agent

- Decision: Current MyGame update systems are marked `[SequentialSystem]` instead of forced into partial summaries.
  Rationale: Their external side effects are thread-affine or unanalyzable today; sequential quarantine is the safe 0.5 migration until those APIs receive explicit scheduler contracts.
  Date/Author: 2026-06-06 / agent

## Future Versions / Backlog

- Future version: Replace most per-method analyzer allowlists and manual access summaries with a generated cross-assembly ECS access manifest.
  Scope: each compiled module analyzes its source-visible helper/facade methods and emits compact metadata such as method identity, generic component argument mapping, and inferred read/write access. Downstream analyzer runs consume those manifests when a method body is metadata-only.
  Rationale: Roslyn cannot fully analyze metadata-only methods without a contract, but hand-maintained analyzer switches do not scale. A generated manifest keeps the contract derived from source, preserves fail-closed behavior when metadata is missing or unprovable, and reduces `[Reads<T>]` / `[Writes<T>]` usage to true escape hatches for external or opaque boundaries.
  Not in 0.5: keep `0.5` focused on fail-closed analysis, generated scheduler metadata, runtime graph integration, and migration. The manifest is a future hardening/simplification step after the first scheduler is accepted.

## Outcomes & Retrospective

`ISystemUpdate` now has a generated scheduler path for 0.5. Startup may allocate and use reflection to find generated providers, but frame execution uses generated descriptors, dense graph arrays, preallocated `ValueJobHandle` buffers, and `JobScheduler` value jobs. Fixed update remains sequential. Existing opaque game systems are quarantined with `[SequentialSystem]`, and the old Dragon `IEcsRunParallel` prototype is retained only as compatibility/baseline coverage behind analyzer restrictions.

## Context And Orientation

Current prototype:

- `Modules/Shared/ECS/ECS.Core/IEcsRunParallel.cs`
- `Modules/Shared/ECS/ECS.Core/SystemExecutionNode.cs`
- `ECS.Core.Tests`

Lifecycle integration:

- `Karpik.Engine.Core/LifeCycle/ISystemUpdate.cs`
- `Karpik.Engine.Core.Runner/Builder.cs`
- `Karpik.Engine.Core.Runner/Runner.cs`
- Dragon extension runners used by the current sequential wrappers

Analyzer and codegen:

- `Tools/StaticAnalyzer/StaticAnalyzer.csproj`
- `Tools/StaticAnalyzer/DiagnosticIds.cs`
- create `Tools/StaticAnalyzer.Tests/StaticAnalyzer.Tests.csproj`
- `Karpik.Engine.Core.Generator/Karpik.Engine.Core.Codegen/Karpik.Engine.Core.Codegen.csproj`

Create focused scheduler types under `Karpik.Engine.Core/Scheduling/`:

- attributes and mode enum;
- generated metadata contracts;
- compact node and edge storage;
- graph builder used at initialization;
- update runner using `Karpik.Jobs`;
- graph dump diagnostics.

## Real-Time Assessment

Graph construction may allocate at startup. Per-frame scheduling may not. Frame execution traverses dense generated arrays and reserved descriptor slots only.

Do not traverse `Type`, `HashSet<Type>`, `List<T>`, reflection metadata, LINQ, or dynamically grown collections during update execution. Convert component types and systems to stable integer IDs during generated registry initialization.

## Plan Of Work

Implement the scheduler in five reviewable milestones. Preserve the sequential fallback until generated metadata and runtime tests pass.

## Milestones

### Milestone 1: Public metadata and diagnostics

Add attributes:

- `[SequentialSystem]`;
- `[Reads<T>]`;
- `[Writes<T>]`;
- `[RunsAfter<TSystem>]`;
- `[RunsBefore<TSystem>]`.

Define diagnostics with actionable messages:

- update system has opaque unsummarized call;
- explicit summary contradicts inferred access;
- invalid explicit order cycle;
- `[MainThreadOnly]` or graphics command access used from `ISystemUpdate`;
- summary targets unsupported managed component type;
- generated registry missing a registered update system.

### Milestone 2: Roslyn analysis and generated registry

Use Roslyn `IOperation` and control-flow APIs to inspect each `ISystemUpdate.Update()` and reachable source methods.

Supported inference:

- `EcsPool<T>` field, local, parameter, and aspect access means write;
- `EcsReadonlyPool<T>` means read;
- known `DefaultWorld` facade helper calls are summarized;
- source helper methods are traversed;
- library-boundary helpers require explicit `[Reads<T>]` / `[Writes<T>]`.

External calls with no ECS-capable receiver or argument may use a small reviewed pure allowlist, including routine BCL math operations. Any external call that receives a world, pool, aspect, ECS facade, or ECS-derived ref requires an explicit summary unless the analyzer understands that helper directly.

Fail closed for:

- reflection;
- `dynamic`;
- unresolved virtual/interface dispatch;
- delegate invocation with unknown target;
- external assembly call that may touch ECS and lacks summary;
- unsupported generic aliasing;
- `Unsafe` access that may alias ECS storage.

Generate stable registry data:

- system integer ID;
- component integer IDs;
- read and write spans;
- explicit ordering edges;
- sequential marker;
- diagnostic display names outside the frame path.

Generate one provider per compiled module assembly. During startup, aggregate providers for loaded modules into one compact runtime registry and flatten the graph. Startup discovery may allocate; frame traversal may not.

### Milestone 3: Compact update graph

At initialization:

- validate generated metadata covers every registered `ISystemUpdate`;
- build deterministic conflict edges from sorted integer component IDs;
- apply explicit before/after edges;
- detect cycles;
- reserve exact job descriptor and dependency capacities;
- emit stable graph dump.

Conflict rules:

- read/read may overlap;
- read/write, write/read, and write/write serialize;
- `[SequentialSystem]` becomes a barrier in stable registration/topological order.

### Milestone 4: Runtime integration

Replace sequential `ISystemUpdate` execution with the generated scheduler while preserving:

- parallel production-default mode;
- single-thread fallback mode;
- deterministic sequential mode;
- exception propagation to orchestration thread;
- quiesce hook for shutdown and restart-worker hot reload.

Keep fixed update sequential. Do not add `[MainThreadOnly]` update dispatch. Main-thread frame work belongs in `Begin` or `Render`.

Update server overload handling in `Karpik.Engine.Core/CoreRunner.cs`: retain bounded catch-up diagnostics but preserve fixed backlog instead of resetting `nextTickTime` and silently skipping pending ticks.

### Milestone 5: Migration and quarantine

Migrate engine and sample `ISystemUpdate` implementations:

- fix pool types so reads use `EcsReadonlyPool<T>`;
- add helper summaries only at real library boundaries;
- mark intentionally opaque systems `[SequentialSystem]`;
- update analyzer tests for invalid systems.

Quarantine or remove the Dragon `IEcsRunParallel` prototype only after replacement tests pass.

## Concrete Steps

Commands run from `C:\Users\artem\RiderProjects\KarpikEngine`.

```powershell
dotnet test ECS.Core.Tests\ECS.Core.Tests.csproj -m:1 -nr:false
dotnet test Tools\StaticAnalyzer.Tests\StaticAnalyzer.Tests.csproj -m:1 -nr:false
dotnet test Karpik.Engine.Core.Runner.Tests\Karpik.Engine.Core.Runner.Tests.csproj -m:1 -nr:false
dotnet build Modules\Shared\ECS\ECS.Core\ECS.Core.csproj -m:1 -nr:false
dotnet build ServerLauncher\ServerLauncher.csproj -m:1 -nr:false
```

## Validation And Acceptance

Accept when:

- disjoint updates overlap under a barrier-based concurrency test;
- all conflicting combinations serialize;
- explicit ordering and cycle diagnostics work;
- opaque access fails build;
- summaries and `[SequentialSystem]` resolve supported exceptions;
- deterministic and single-thread modes produce stable order;
- reserved-capacity update scheduling measures `0 B/frame`;
- server launcher builds and executes fixed ticks sequentially with parallel update batches.

## Idempotence And Recovery

Keep the sequential update path selectable until scheduler acceptance passes. Keep prototype tests as reference coverage. If generated metadata misses a registered system, fail initialization rather than running it unsafely.

## Artifacts And Notes

Update `docs/02_ADR/scheduler-jobs-runtime.md` with generated metadata precedence, supported analyzer subset, deterministic semantics, and sequential opt-out.

Implemented artifacts so far:

- Scheduler metadata attributes and enums under `Karpik.Engine.Core/Scheduling/`: `SequentialSystemAttribute`, `ReadsAttribute<T>`, `WritesAttribute<T>`, `RunsAfterAttribute<TSystem>`, `RunsBeforeAttribute<TSystem>`, `MainThreadOnlyAttribute`, `EcsAccessMode`, `EcsOrderKind`, and `EcsUpdateSchedulerMode`.
- Runtime generated-registry contracts under `Karpik.Engine.Core/Scheduling/`: `IEcsUpdateRegistryProvider`, `EcsUpdateSystemDescriptor`, `EcsComponentAccessDescriptor`, and `EcsSystemOrderDescriptor`.
- Runtime scheduler and startup compact graph artifacts under `Karpik.Engine.Core/Scheduling/`: `EcsUpdateScheduler`, `EcsUpdateGraph`, `EcsUpdateGraphNode`, `EcsUpdateGraphBuilder`, and `EcsUpdateGraphBuildException`.
- Runner integration: `DragonExtensions/LifeCycleBridge.cs`, `Karpik.Engine.Core.Runner/Runner.cs`, and `Karpik.Engine.Core.Runner/Program.cs`.
- Runtime scheduler tests: `Karpik.Engine.Core.Runner.Tests/EcsUpdateSchedulerRuntimeTests.cs`.
- Migrated sequential update systems: `MyGame/Client/MyGame.Client.Main/DemoModuleClient.cs`, `MyGame/Client/MyGame.Client.Main/Systems/ApplySpriteSystem.cs`, `DisplaySystem.cs`, `InputSystem.cs`, `MyGame/Server/MyGame.Server.Main/Systems/NetworkSystem.cs`, and `ServerCollisionEventSystem.cs`.
- Roslyn analyzer implementation and tests: `Tools/StaticAnalyzer/ECS/003.UpdateScheduler/EcsUpdateSchedulerAnalyzer.cs`, `Tools/StaticAnalyzer.Tests/EcsUpdateSchedulerAnalyzerTests.cs`, and helper changes in `Tools/StaticAnalyzer.Tests/AnalyzerTestHarness.cs`.
- Incremental generator implementation and tests: `Karpik.Engine.Core.Generator/Karpik.Engine.Core.Codegen/EcsUpdateRegistryGenerator.cs`, `Tools/StaticAnalyzer.Tests/EcsUpdateRegistryGeneratorTests.cs`, and the codegen project reference in `Tools/StaticAnalyzer.Tests/StaticAnalyzer.Tests.csproj`.
- Graph builder tests: `ECS.Core.Tests/EcsUpdateGraphBuilderTests.cs`.
