# v0.5

## Key Changes
* **Native Memory Foundation:** Added `Karpik.Memory` with explicit unmanaged ownership, borrowed views, caller-owned result handles, fixed-capacity pools, linear allocation, and debug diagnostics for stale or invalid access.
* **No-GC Jobs Runtime:** Added a standalone value-job `JobScheduler` for `IJob` and `IJobFor` workloads with native descriptor storage, bounded worker queues, work stealing, dependency tracking, profiler hooks, and `0 B` managed allocation on measured steady-state paths after warm-up.
* **ECS Update Scheduler:** Replaced the planned `v0.4` sequential `ISystemUpdate` path with a generated, graph-based scheduler. `ISystemUpdate` is now parallel-by-default when component access is known and non-conflicting.
* **Scheduler Analyzer and Codegen:** Added static scheduler metadata, Roslyn validation, and a generated per-assembly update registry so unsafe or opaque update systems fail closed instead of running in parallel by accident.
* **Runner Integration:** Integrated the update scheduler into `EngineRunner` while preserving existing Dragon wrapper ordering and DI behavior. Production mode uses `JobScheduler` value jobs; deterministic and single-thread modes remain selectable.
* **Server Fixed Tick Backlog:** Updated server overload handling so bounded catch-up diagnostics remain, but pending fixed-tick backlog is preserved instead of being silently discarded.

## Native Memory
* Added the `Karpik.Memory` project and focused tests.
* Added aligned native allocation ownership through `NativeAllocation`, `NativeMemoryDiagnostics`, and generation tokens.
* Added `NativeArray<T>`, `NativeSlice<T>`, `NativeResult<T>`, and `NativeResultHandle<T>` for unmanaged storage and borrowed hot-path access.
* Added `NativeLinearAllocator`, `NativePool<T>`, and `NativeArena`.
* Added debug coverage for invalid lengths, bounds failures, stale borrowed views, stale result handles, double dispose, double return, and disposal invalidation.
* Migrated `Karpik.Jobs/SimpleNativeArray<T>` to wrap `NativeArray<T>` while preserving the old compatibility surface.
* Recorded Release allocation evidence: preallocated `NativeArray<T>` traversal, `NativeLinearAllocator` allocate/reset, `NativePool<T>` rent/return, and `NativeResult<T>` handle writes measured `0 B` managed allocation after warm-up.
* Marked `NativeArena` as an outside-frame allocator because block growth/reset can allocate managed metadata.

## Jobs
* Added value-job contracts `IJob` and `IJobFor`.
* Added `JobScheduler` APIs: `TrySchedule`, `TryScheduleParallel`, `Schedule`, `ScheduleParallel`, and `Complete`.
* Added `ValueJobHandle` over native descriptor identity with stale-handle invalidation.
* Added native descriptor and payload storage, fixed dependency storage, pending completion checks, completed/failed generation tracking, and cold-path exception reporting.
* Added bounded native `WorkStealingDeque<T>` and worker publication through `TryPublish`, `TryRunNext`, owner pop, cross-worker steal, dependency requeue, and capacity failure reporting.
* Added worker runtime startup/shutdown, wake-up on publish/completion, stopped-publication rejection, and volatile completion visibility.
* Added optional profiler events through `JobProfilerEvent` and `JobBatchInfo`.
* Isolated the old delegate-based `JobSystem` as an allocating compatibility path and marked it with `AllocatingCompatibilityAttribute`.
* Added tests proving `JobScheduler` does not reference legacy delegate runtime types such as `JobSystem`, `JobHandle`, `JobWrapper`, `JobCompletion`, `CancellationTokenSource`, or `ConcurrentQueue<JobWrapper>`.

## ECS Update Scheduling
* Added scheduler metadata attributes: `[SequentialSystem]`, `[Reads<T>]`, `[Writes<T>]`, `[RunsAfter<TSystem>]`, `[RunsBefore<TSystem>]`, and `[MainThreadOnly]`.
* Added runtime scheduler contracts and graph artifacts under `Karpik.Engine.Core/Scheduling`: `IEcsUpdateRegistryProvider`, `EcsUpdateSystemDescriptor`, `EcsComponentAccessDescriptor`, `EcsSystemOrderDescriptor`, `EcsUpdateGraph`, `EcsUpdateGraphNode`, `EcsUpdateGraphBuilder`, `EcsUpdateGraphBuildException`, `EcsUpdateScheduler`, and `EcsUpdateSchedulerMode`.
* Added `EcsUpdateGraphBuilder`, which validates registered update systems against generated descriptors, flattens system and component types to dense IDs, builds conflict/order/sequential dependencies, detects explicit order cycles, and stores compact graph arrays.
* Added `EcsUpdateScheduler`, which builds the graph once during initialization and reuses it each frame.
* Added parallel execution through `JobScheduler` value jobs with preallocated handle and dependency buffers.
* Added deterministic topological execution and single-thread registration-order execution modes for debugging and reproducible validation.
* Kept `ISystemFixedUpdate` sequential in `v0.5`; fixed simulation is not parallelized by this release.
* Kept the old Dragon `IEcsRunParallel` prototype as compatibility and baseline coverage only. User assemblies are still blocked from raw Dragon lifecycle APIs by analyzer rules.

## Static Analysis and Code Generation
* Added `EcsUpdateSchedulerAnalyzer` checks for `ISystemUpdate.Update()`.
* Direct `EcsPool<T>` access is inferred as write access; `EcsReadonlyPool<T>` access is inferred as read access.
* Source helper calls are traversed and helper summaries are validated.
* Metadata-only external helpers require explicit `[Reads<T>]` or `[Writes<T>]` summaries.
* Delegate, dynamic, unresolved virtual/interface dispatch, reflection through `typeof(...)`, unsafe address-of, lifecycle facade calls, and unsupported managed component summaries are treated as opaque scheduled-update access.
* Reviewed BCL math calls remain accepted.
* Reviewed world facade summaries were added for Dragon `EcsWorld.GetPool<T>()` and `GetPoolUnchecked<T>()`, plus wrapper `World.Get<T>()`, `Has<T>()`, `TryGet<T>()`, `Add<T>()`, `Set<T>()`, `Del<T>()`, and `Event<T>()`.
* Added aspect inference for `Where<TAspect>()`: `EcsReadonlyPool<T>` fields infer read access and `EcsPool<T>` fields infer write access.
* Added `EcsUpdateRegistryGenerator`, which emits a generated `IEcsUpdateRegistryProvider` per assembly from explicit scheduler metadata.

## Runner and Game Migration
* Moved scheduler contracts, generated registry contracts, and graph builder to `Karpik.Engine.Core/Scheduling` to avoid a `Runner -> ECS.Core -> Runner` project cycle.
* Updated `EngineRunner` to extract sorted `ISystemUpdate` instances from existing Dragon `UpdateSystem` wrappers and initialize `EcsUpdateScheduler`.
* Updated frame execution so `EngineRunner` calls `EcsUpdateScheduler.Update()` instead of the old sequential update runner.
* Preserved Dragon wrapper layer/order sorting and DI behavior during migration.
* Marked current opaque game update systems `[SequentialSystem]`, including ImGui/input/network/physics/lifecycle-facade style systems, until their external APIs are reviewed and summarized.
* Updated server fixed-loop overload handling to preserve pending fixed-tick backlog while retaining bounded catch-up diagnostics.

## Verification
* `dotnet test Karpik.Memory.Tests\Karpik.Memory.Tests.csproj -c Release -m:1 -nr:false` passed.
* `dotnet test ECS.Core.Tests\ECS.Core.Tests.csproj -m:1 -nr:false --no-restore` passed.
* `dotnet test Karpik.Engine.Core.Runner.Tests\Karpik.Engine.Core.Runner.Tests.csproj -m:1 -nr:false --no-restore` passed, including parallel overlap, conflict serialization, deterministic order, and `0 B` calling-thread allocation after warm-up.
* `dotnet test Tools\StaticAnalyzer.Tests\StaticAnalyzer.Tests.csproj -m:1 -nr:false --no-restore` passed with only the existing `NU1900` vulnerability-feed warning from unavailable NuGet network access.
* `dotnet build ServerLauncher\ServerLauncher.csproj -m:1 -nr:false --no-restore` passed.
* `dotnet build ClientLauncher\ClientLauncher.csproj -m:1 -nr:false --no-restore` passed.

## Upgrade Notes
* `ISystemUpdate` systems are scheduled by default. If a system performs thread-affine work, opaque external calls, lifecycle facade calls, reflection, unsafe aliasing, input, networking, ImGui, or other side-effect-heavy operations, mark it `[SequentialSystem]` until its access is explicitly summarized and reviewed.
* Prefer explicit `[Reads<T>]` and `[Writes<T>]` metadata for helpers and systems whose ECS access is not directly inferable.
* Do not use the old Dragon `IEcsRunParallel` path in game or module code. Use engine `ISystemUpdate` and scheduler metadata instead.
* Do not use legacy delegate `JobSystem` APIs in new hot paths. Use `JobScheduler` value jobs and caller-owned native storage.
* Do not use `NativeArena` as a steady-state frame allocator. Use preallocated `NativeArray<T>`, `NativeSlice<T>`, `NativeResult<T>`, `NativeLinearAllocator`, or `NativePool<T>` where appropriate.
* `ISystemFixedUpdate` remains sequential in this release. Physics and fixed-step gameplay should still use fixed dt and must not assume parallel fixed execution.
* The threaded client simulation/render pipeline is not part of the completed foundation slice yet. `ISystemRenderPrepare`, input snapshot/ring, and Graphics.Core triple-buffer ownership remain planned separately.
