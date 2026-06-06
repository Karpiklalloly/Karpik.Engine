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
- [ ] Continue Roslyn hardening for unsafe/generic alias edge cases, lifecycle world facade methods, and migrated-system coverage.
- [ ] Generate compact metadata registry and per-phase update graph.
- [ ] Integrate `ISystemUpdate` scheduler with parallel, deterministic, and single-thread modes.
- [ ] Migrate update systems and quarantine the Dragon prototype.
- [ ] Run scheduler acceptance gate.

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

## Future Versions / Backlog

- Future version: Replace most per-method analyzer allowlists and manual access summaries with a generated cross-assembly ECS access manifest.
  Scope: each compiled module analyzes its source-visible helper/facade methods and emits compact metadata such as method identity, generic component argument mapping, and inferred read/write access. Downstream analyzer runs consume those manifests when a method body is metadata-only.
  Rationale: Roslyn cannot fully analyze metadata-only methods without a contract, but hand-maintained analyzer switches do not scale. A generated manifest keeps the contract derived from source, preserves fail-closed behavior when metadata is missing or unprovable, and reduces `[Reads<T>]` / `[Writes<T>]` usage to true escape hatches for external or opaque boundaries.
  Not in 0.5: keep `0.5` focused on fail-closed analysis, generated scheduler metadata, runtime graph integration, and migration. The manifest is a future hardening/simplification step after the first scheduler is accepted.

## Outcomes & Retrospective

No outcome yet.

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

Create focused scheduler types under `Modules/Shared/ECS/ECS.Core/Scheduling/`:

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
