# Deliver the 0.5 scheduler, jobs, memory, and threaded client foundation

This ExecPlan is a living document. It must be maintained according to `plans/PLANS.md`.

## Purpose / Big Picture

Release `0.5` as the real-time execution foundation for KarpikEngine.

The release is complete only when unmanaged memory primitives, standalone no-GC jobs, parallel-by-default `ISystemUpdate` execution, and the threaded client/render pipeline work together without managed allocations after warm-up. The server uses the same update scheduler without adopting the client-specific render and input pipeline.

This master plan records the cross-cutting contract and release gates. Implementation proceeds through four self-contained child ExecPlans in strict order:

1. [`plans/native-memory-foundation-execplan.md`](native-memory-foundation-execplan.md)
2. [`plans/jobs-runtime-execplan.md`](jobs-runtime-execplan.md)
3. [`plans/ecs-update-scheduler-execplan.md`](ecs-update-scheduler-execplan.md)
4. [`plans/client-threading-render-pipeline-execplan.md`](client-threading-render-pipeline-execplan.md)

Do not begin a later child against temporary APIs from unfinished earlier children. Update this master plan after every child plan closes.

## Progress

- [x] (2026-06-02 23:32 +04:00) Initial broad `0.5` draft created from the roadmap and current prototypes.
- [x] (2026-06-03 00:33 +04:00) Design discussion completed: scope, threading, scheduler safety, graphics ownership, overload behavior, memory primitives, and child-plan order agreed.
- [x] (2026-06-03 00:33 +04:00) Split the broad draft into one master ExecPlan and four ordered child ExecPlans.
- [ ] Complete `plans/native-memory-foundation-execplan.md`.
- [ ] Complete `plans/jobs-runtime-execplan.md`.
- [ ] Complete `plans/ecs-update-scheduler-execplan.md`.
- [ ] Complete `plans/client-threading-render-pipeline-execplan.md`.
- [ ] Run the `0.5` release gate and update the roadmap and ADRs.

## Surprises & Discoveries

- Observation: The current `Karpik.Jobs` enqueue path allocates `CancellationTokenSource`, `JobCompletion`, closures, delegates, and dependency continuations for short-lived work.
  Evidence: `Karpik.Jobs/JobSystem.cs`, especially `EnqueueInternal`, `EnqueueParallelInternal`, and `Enqueue<T>`.

- Observation: A minimal unmanaged container already exists, but it has no bounds checks, ownership token, stale-handle protection, or leak diagnostics.
  Evidence: `Karpik.Jobs/SimpleNativeArray.cs`.

- Observation: The ECS parallel runner is a useful isolated prototype, but its startup graph uses reflection object graphs and its conflict builder allocates a temporary `HashSet<Type>`.
  Evidence: `Modules/Shared/ECS/ECS.Core/IEcsRunParallel.cs` and `SystemExecutionNode.cs`.

- Observation: Graphics already has the intended basic shape: thread-local command buffers, an asynchronous merge thread, and main-thread submit/present systems. `0.5` must extend it rather than add a second render abstraction.
  Evidence: `Modules/Client/Graphics/Graphics.Core/Context/GraphicsContext.cs`, `Buffers/ThreadBuffer.cs`, `Merge/MergeThread.cs`, and `ECS/GraphicsCoreBeginSystem.cs`.

- Observation: Current graphics buffering is not sufficient for a decoupled simulation thread. `GraphicsContext` uses two per-thread buffers and a lock-protected global list; `MergeThread.BeginMerge()` waits when the previous merge is incomplete.
  Evidence: `GraphicsContext.Buffer`, `GraphicsContext.BeginFrame`, and `MergeThread.BeginMerge`.

- Observation: Current input state is unsuitable for a cross-thread no-GC boundary. It uses `ConcurrentDictionary`, LINQ, mutable `List<T>` buffers, event delegates, and direct `IInputSource` queries.
  Evidence: `Modules/Client/Input/Input.cs`.

## Decision Log

- Decision: Deliver the full `0.5` scope through four child ExecPlans in bottom-up order: memory, jobs, ECS scheduler/codegen, client threading/render.
  Rationale: Each layer must be correct and measurable before the next layer depends on it. Mixing all four changes in one implementation stream would make allocation and race regressions difficult to isolate.
  Date/Author: 2026-06-03 / developer and agent

- Decision: Add a standalone `Karpik.Memory` project with unmanaged-first allocators and containers.
  Rationale: Unsafe storage ownership should be isolated from scheduler logic. `Karpik.Jobs` depends on memory primitives, not the reverse.
  Date/Author: 2026-06-03 / developer and agent

- Decision: The no-GC jobs API uses value-type `IJob` and `IJobFor`; scheduling and completion belong to one orchestration thread; workers only execute.
  Rationale: Native descriptors, stable dependency ownership, and predictable allocation behavior are substantially simpler under a single-producer contract. Delegate APIs remain compatibility-only and allocating.
  Date/Author: 2026-06-03 / developer and agent

- Decision: Parallel scheduling is production-default only for `ISystemUpdate`.
  Rationale: `ISystemFixedUpdate` remains sequential until a later release. `Begin`, `LateUpdate`, and `Render` have thread ownership semantics that should not be hidden behind generic parallelization.
  Date/Author: 2026-06-03 / developer and agent

- Decision: Every `ISystemUpdate` is scheduled by default and must be statically analyzable or explicitly marked `[SequentialSystem]`.
  Rationale: Unknown access must not silently reduce safety or parallelism. Opaque helper calls require explicit `[Reads<T>]` and `[Writes<T>]` summaries or a sequential opt-out.
  Date/Author: 2026-06-03 / developer and agent

- Decision: Client main thread owns platform events, input collection, graphics submit, ImGui, and present. A simulation thread owns fixed ticks, `ISystemUpdate`, and demand-driven `ISystemRenderPrepare`.
  Rationale: Platform and backend work remain on the OS thread while gameplay execution can scale independently. Fixed ticks stay sequential and are never silently dropped.
  Date/Author: 2026-06-03 / developer and agent

- Decision: Add read-only parallel `ISystemRenderPrepare`; forbid graphics command writes from `ISystemUpdate`.
  Rationale: Simulation and presentation preparation have different cadence. Catch-up fixed ticks and collapsed update requests must not build frames that cannot be shown.
  Date/Author: 2026-06-03 / developer and agent

- Decision: Reuse Graphics.Core `DrawCommand`, `ThreadBuffer`, `GraphicsContext.Buffer`, and `MergeThread`; add sort keys and triple-buffer ownership.
  Rationale: The existing client-only renderer already has thread-local CPU command recording and async merge. A parallel graphics abstraction would duplicate working concepts.
  Date/Author: 2026-06-03 / developer and agent

## Outcomes & Retrospective

No implementation outcome yet. Update after each child ExecPlan closes.

## Context and Orientation

The `0.4` lifecycle foundation is documented in `docs/04_Roadmap/0.4-system-lifecycle-plan.md`. It intentionally left public scheduler integration for `0.5`.

`Karpik.Jobs/JobSystem.cs` currently owns worker threads and compatibility enqueue APIs. It is not suitable for the new frame path because every short job allocates managed state.

`Modules/Shared/ECS/ECS.Core/IEcsRunParallel.cs` and `SystemExecutionNode.cs` are prototype references only. The public target is Karpik `ISystemUpdate`, not Dragon `IEcsRunParallel`.

`Karpik.Engine.Core.Runner/Runner.cs` currently executes lifecycle phases sequentially in `EngineRunner.Run`. `Karpik.Engine.Core/CoreRunner.cs` owns client and server loops. Restart-worker state capture is documented in `plans/restart-worker-hot-reload-execplan.md`.

`Modules/Client/Graphics/Graphics.Core` is the existing client renderer. `DrawCommand` points into per-type arrays in `ThreadBuffer`; `MergeThread` converts pending CPU commands into backend command lists; `GraphicsCoreSubmitSceneSystem`, `ImGuiRenderSystem`, and `GraphicsCoreSwapBuffersSystem` run during `ISystemRender`.

Terms:

- Orchestration thread: the thread allowed to build dependency batches, call `Schedule`, and wait for completion.
- Simulation thread: the client orchestration thread for fixed ticks, variable update, and render preparation.
- Sequential barrier: an update node intentionally excluded from workers through `[SequentialSystem]`.
- Opaque access: code whose ECS pool behavior cannot be proven from available source and explicit helper summaries.
- Render command set: all thread-local `Graphics.Core` buffers belonging to one prepared frame.

## Real-Time Assessment

This plan touches fixed ticks, variable update, ECS pool access, job submission, input collection, render preparation, merge, submit, shutdown, and hot reload barriers.

The steady-state managed allocation budget is `0 B/frame` after warm-up for:

- memory container usage;
- no-GC job scheduling, dependency tracking, completion, and work stealing;
- ECS update graph traversal;
- input snapshot publication and event-ring processing;
- render command recording, sort, merge, submit, and repeated submission of the last completed frame.

Hot paths use dense arrays, native storage, stable integer IDs, bounded queues, and preallocated scratch buffers. Reflection, LINQ, closures, delegates, dynamic collection growth, and hidden fallback allocation are forbidden in these paths.

Client / Server / Shared boundaries remain strict. Graphics and input types stay in Client. Shared scheduler contracts must not reference Veldrid or presentation types.

## Runtime Data Flow

Client:

```text
OS/main thread
  collect platform events
  publish latest held/axes input snapshot
  append edge/text/device events to bounded SPSC ring
  increment requested-frame generation
  submit last completed Graphics.Core command list
  render ImGui
  present

Simulation thread
  observe latest requested-frame generation and collapse intermediate requests
  run bounded sequential fixed-tick catch-up without dropping backlog
  run parallel ISystemUpdate graph
  when renderer requested a frame and a render-command set is free:
    run read-only parallel ISystemRenderPrepare
    publish Graphics.Core thread-local buffers to MergeThread

MergeThread
  sort DrawCommand entries by precomputed sort key without GC
  build next backend command list
  publish completed list for main-thread submit
```

Server:

```text
server orchestration loop
  run bounded sequential fixed ticks
  run shared parallel ISystemUpdate graph
```

The server does not adopt client frame requests, Graphics.Core, input snapshots, or render buffers.

## Overload And Failure Policies

- Fixed ticks: process at most the configured catch-up count per simulation iteration, preserve backlog, and emit diagnostics. Never skip ticks silently.
- Variable updates: collapse intermediate main-thread frame requests to the latest generation.
- Input ring: keep a correct prefix, drop new events while full, count overflow, then publish `Overflow` and resynchronize transient state from the latest held/axes snapshot.
- ECS jobs: reserve exact descriptor capacity when building the graph. Capacity exhaustion is a configuration error.
- Standalone jobs: allow a native overflow allocation fallback with counters and diagnostics. Never allocate on the managed heap.
- Graphics buffers: fail fast with system ID, command type, configured capacity, and required capacity. Resize only outside an active frame.
- Reload and shutdown: stop publishing batches, complete the current update batch and fixed tick, then serialize ECS. Timeout triggers emergency worker restart without a new snapshot.

## Plan Of Work

Execute the child plans in order. Each child plan must close its validation gate and update this master `Progress` section before the next child starts.

After all four children close:

1. Run the full solution tests and sequential launcher builds.
2. Perform repeated client and server restart-worker smoke cycles.
3. Record benchmark and allocation evidence in child plans and summarize it here.
4. Update `docs/04_Roadmap/karpikengine-1.0-roadmap.md`.
5. Add or update ADRs for memory ownership, scheduler/codegen safety, and client threading/render ownership.

## Milestones

### Milestone 1: Native memory foundation

Complete `plans/native-memory-foundation-execplan.md`.

Gate:

- `NativeArena`, `NativeLinearAllocator`, `NativePool<T>`, `NativeArray<T>`, and `NativeResult<T>` exist in `Karpik.Memory`;
- debug safety checks catch stale access, bounds failures, leaks, and double disposal;
- Release benchmarks record zero managed allocations for preallocated use.

### Milestone 2: Standalone jobs runtime

Complete `plans/jobs-runtime-execplan.md`.

Gate:

- value-type jobs, dependencies, batch scheduling, caller-owned results, work stealing, completion, exceptions, shutdown, profiler hooks, and native overflow diagnostics pass focused tests;
- no-GC scheduling measures `0 B` managed allocation after warm-up;
- throughput and tail latency are recorded for `1`, `2`, `4`, and `8` workers where hardware permits.

### Milestone 3: ECS update scheduler and codegen

Complete `plans/ecs-update-scheduler-execplan.md`.

Gate:

- `ISystemUpdate` is parallel-by-default;
- generated metadata and compact graphs replace runtime reflection for the frame path;
- opaque access fails build unless explicitly sequential or summarized;
- conflicting systems serialize, disjoint systems overlap, deterministic and single-thread modes pass;
- frame scheduling measures `0 B/frame`.

### Milestone 4: Threaded client and render pipeline

Complete `plans/client-threading-render-pipeline-execplan.md`.

Gate:

- main-thread input/render ownership and simulation-thread fixed/update/render-prepare ownership are enforced;
- input snapshot/ring overflow recovery works;
- Graphics.Core uses read-only parallel `ISystemRenderPrepare`, no-GC sort keys, `MergeThread`, and triple buffering;
- normal present never waits for simulation;
- repeated reload smoke passes.

### Milestone 5: Release gate

Run all validation commands and document results.

## Concrete Steps

Commands run from `C:\Users\artem\RiderProjects\KarpikEngine`.

After the memory child:

```powershell
dotnet test Karpik.Memory.Tests\Karpik.Memory.Tests.csproj -m:1 -nr:false
dotnet build Karpik.Jobs\Karpik.Jobs.csproj -m:1 -nr:false
```

After the jobs child:

```powershell
dotnet test Karpik.Jobs.Tests\Karpik.Jobs.Tests.csproj -m:1 -nr:false
dotnet build Karpik.Jobs\Karpik.Jobs.csproj -m:1 -nr:false
```

After the ECS scheduler child:

```powershell
dotnet test ECS.Core.Tests\ECS.Core.Tests.csproj -m:1 -nr:false
dotnet test Tools\StaticAnalyzer.Tests\StaticAnalyzer.Tests.csproj -m:1 -nr:false
dotnet test Karpik.Engine.Core.Runner.Tests\Karpik.Engine.Core.Runner.Tests.csproj -m:1 -nr:false
```

After the threaded client child:

```powershell
dotnet test Modules\Client\Graphics\Graphics.Core.Tests\Graphics.Core.Tests.csproj -m:1 -nr:false
dotnet test Karpik.Engine.Core.Runner.Tests\Karpik.Engine.Core.Runner.Tests.csproj -m:1 -nr:false
dotnet build ClientLauncher\ClientLauncher.csproj -m:1 -nr:false
dotnet build ServerLauncher\ServerLauncher.csproj -m:1 -nr:false
```

Final automated gate:

```powershell
dotnet test KarpikEngine.slnx -m:1 -nr:false
dotnet build ClientLauncher\ClientLauncher.csproj -m:1 -nr:false
dotnet build ServerLauncher\ServerLauncher.csproj -m:1 -nr:false
```

Run launcher builds sequentially. This workspace has previously observed output races when independent build processes target shared dependencies.

Manual gate:

- start the client launcher and verify input, fixed simulation, parallel update, render preparation, merge, submit, present, and shutdown;
- force simulation delay and verify the renderer repeats the last completed frame without waiting;
- overflow a test input ring and verify `Overflow` resynchronization;
- reload client and server workers at least five times each;
- verify no stale jobs, buffers, callbacks, or process-local resources touch destroyed ECS worlds.

## Validation And Acceptance

Accept `0.5` only when all child gates and the final automated and manual gates pass. A child plan that records missing benchmark evidence, hidden managed allocation, unbounded waits, or unresolved races blocks the release.

## Idempotence And Recovery

Child plans are milestone-isolated. Keep previous paths until replacement tests pass:

- do not delete `SimpleNativeArray<T>` until `Karpik.Memory` migration compiles;
- keep delegate jobs as compatibility-only until value jobs are integrated;
- keep the sequential lifecycle path as fallback until scheduler tests pass;
- keep current Graphics.Core submission behavior behind a fallback until triple-buffer smoke passes.

If a child exposes a contract flaw in an earlier child, update the earlier child plan and rerun its gate before continuing.

## Artifacts And Notes

Required child plans:

- `plans/native-memory-foundation-execplan.md`
- `plans/jobs-runtime-execplan.md`
- `plans/ecs-update-scheduler-execplan.md`
- `plans/client-threading-render-pipeline-execplan.md`

Expected ADRs before close:

- `docs/02_ADR/native-memory-ownership.md`
- `docs/02_ADR/scheduler-jobs-runtime.md`
- `docs/02_ADR/client-threading-render-pipeline.md`
