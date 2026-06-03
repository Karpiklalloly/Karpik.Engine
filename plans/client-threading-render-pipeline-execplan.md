# Separate client simulation and render ownership

This ExecPlan is a living document. It must be maintained according to `plans/PLANS.md`.

## Purpose / Big Picture

Move client simulation onto a dedicated thread while keeping platform events, input collection, graphics submit, ImGui, and present on the OS/main thread. Add demand-driven read-only parallel `ISystemRenderPrepare` and extend the existing Graphics.Core command pipeline with sort keys and triple-buffer ownership.

The renderer repeats the last completed frame when simulation or merge is late. Normal present does not wait for simulation.

## Progress

- [x] (2026-06-03 00:33 +04:00) Design agreed as child plan 4 of `plans/scheduler-jobs-memory-execplan.md`.
- [ ] Record current client input, graphics, loop, merge, and reload behavior.
- [ ] Add lifecycle contract and analyzer rules for `ISystemRenderPrepare`.
- [ ] Replace input internals with snapshot plus bounded SPSC event ring.
- [ ] Split client main-thread and simulation-thread loops.
- [ ] Extend Graphics.Core with sorted triple-buffer command-set ownership.
- [ ] Migrate client drawing systems from `ISystemRender` to `ISystemRenderPrepare`.
- [ ] Run allocation, overload, shutdown, and repeated-reload gates.

## Surprises & Discoveries

- Observation: Graphics.Core already records commands into thread-local `ThreadBuffer` instances and merges asynchronously.
  Evidence: `GraphicsContext.cs`, `ThreadBuffer.cs`, and `MergeThread.cs`.

- Observation: Current graphics ownership is only double-buffered per writer thread and globally coordinated with `lock`. Merge can block `BeginMerge()` waiting for the previous build.
  Evidence: `GraphicsContext.Buffer`, `GraphicsContext.BeginFrame`, and `MergeThread.BeginMerge`.

- Observation: Current sample drawing queries ECS during `ISystemRender`, so it must move to render preparation before ECS and backend ownership can be separated.
  Evidence: `MyGame/Client/MyGame.Client.Main/Systems/DrawSpriteSystems.cs`.

- Observation: Current input service uses managed dictionaries, lists, LINQ, events, and direct source reads. It is not a snapshot boundary and is not steady-state no-GC.
  Evidence: `Modules/Client/Input/Input.cs`.

## Decision Log

- Decision: Main thread owns platform events, input publication, backend submit, ImGui, and present. Simulation thread owns fixed ticks, parallel update, and render preparation.
  Rationale: Graphics backend and platform affinity remain explicit while gameplay work scales independently.
  Date/Author: 2026-06-03 / developer and agent

- Decision: Add `ISystemRenderPrepare`, a read-only ECS phase that may write only through `GraphicsContext.Buffer`.
  Rationale: Rendering preparation has display cadence, not simulation cadence. It must not mutate gameplay state.
  Date/Author: 2026-06-03 / developer and agent

- Decision: Preserve existing Graphics.Core commands and `MergeThread`; add sort keys and triple buffering.
  Rationale: Existing command recording and async merge are already aligned with the target architecture.
  Date/Author: 2026-06-03 / developer and agent

- Decision: Use latest held/axes snapshot plus bounded SPSC event ring for edges, text, and device lifecycle.
  Rationale: Snapshots make state reads cheap; the ring preserves short transitions while variable frame requests collapse.
  Date/Author: 2026-06-03 / developer and agent

- Decision: On input-ring overflow, drop new events, preserve the correct prefix, emit `Overflow`, and resynchronize from the latest snapshot.
  Rationale: Overwriting old events can produce impossible edge sequences. Main thread must never block on input publication.
  Date/Author: 2026-06-03 / developer and agent

## Outcomes & Retrospective

No outcome yet.

## Context And Orientation

Lifecycle:

- `Karpik.Engine.Core/LifeCycle/IBuilder.cs`
- create `Karpik.Engine.Core/LifeCycle/ISystemRenderPrepare.cs`
- `Karpik.Engine.Core.Runner/Builder.cs`
- `Karpik.Engine.Core.Runner/Runner.cs`
- `Karpik.Engine.Core/CoreRunner.cs`

Input:

- `Modules/Client/Input/Input.cs`
- `Modules/Client/Input/Systems/UpdateSystem.cs`
- `Modules/Client/Input/InputModuleEcs.cs`
- create snapshot, SPSC ring, and event files under `Modules/Client/Input/`

Graphics:

- `Modules/Client/Graphics/Graphics.Core/Commands/DrawCommand.cs`
- `Modules/Client/Graphics/Graphics.Core/Buffers/ThreadBuffer.cs`
- `Modules/Client/Graphics/Graphics.Core/Context/GraphicsContext.cs`
- `Modules/Client/Graphics/Graphics.Core/Merge/MergeThread.cs`
- `Modules/Client/Graphics/Graphics.Core/ECS/GraphicsCoreBeginSystem.cs`
- `Modules/Client/Graphics/Graphics.Core/ECS/GraphicsCoreModule.cs`
- `Modules/Client/Graphics/Graphics.Core.Tests`

Sample migration:

- `MyGame/Client/MyGame.Client.Main/Systems/DrawSpriteSystems.cs`
- `MyGame/Client/MyGame.Client.Main/Systems/FlushDrawersSystem.cs`

Hot reload:

- `plans/restart-worker-hot-reload-execplan.md`
- `Karpik.Engine.Core/ProcessManagement`

## Real-Time Assessment

This plan changes client frame loops, input update, ECS presentation reads, graphics command recording, sort, merge, submit, and reload barriers.

Required `0 B/frame` managed allocation after warm-up:

- input collection/publication and simulation consumption;
- frame-generation publication;
- render-command recording;
- per-command sort-key storage;
- sort scratch usage;
- merge;
- repeated submit of the last completed frame.

No main-thread blocking is allowed during ordinary present. Shutdown and reload barriers are explicit exceptional paths.

## Runtime Data Flow

```text
main thread
  collect platform events
  publish input snapshot and SPSC events
  increment requested-frame generation
  submit last completed command list
  run ImGui
  present

simulation thread
  collapse requested-frame generations
  process bounded sequential fixed backlog
  run parallel ISystemUpdate
  when requested and a command set is free:
    run parallel read-only ISystemRenderPrepare
    publish prepared Graphics.Core buffers

merge thread
  consume prepared buffers
  sort DrawCommand values by sortKey into preallocated scratch
  build backend command list
  publish completed command list
```

## Plan Of Work

Implement the client boundary in six reviewable milestones. Keep the current sequential client loop and Graphics.Core submission path selectable until the final smoke gate passes.

## Milestones

### Milestone 1: Render-prepare lifecycle and analyzer boundary

Add `ISystemRenderPrepare` and builder/runner support. Its graph is parallel and read-only:

- `EcsReadonlyPool<T>` and known read-only world facade calls are allowed;
- `EcsPool<T>`, component mutation, and world mutation fail analyzer validation;
- `GraphicsContext.Buffer` command writes are allowed;
- graphics command writes from `ISystemUpdate` fail analyzer validation.

Reuse the generated scheduling infrastructure from `plans/ecs-update-scheduler-execplan.md`. Render preparation has its own graph and reserved capacity.

### Milestone 2: Input boundary

Replace hot-path mutable dictionaries and lists with:

- preallocated latest held/axes snapshot;
- double-buffered snapshot publication or versioned copy protocol;
- bounded SPSC ring for key/button pressed/released, text input, and device connect/disconnect;
- `Overflow` marker;
- counters and rate-limited diagnostics;
- simulation-side transient edge reset and snapshot resynchronization.

Release overflow policy:

- do not block main thread;
- preserve ring prefix;
- drop new events while full;
- append `Overflow` after space becomes available;
- clear transient simulation input state and re-read latest held/axes.

### Milestone 3: Client loop split

Refactor `CoreRunner.ClientLoop` and runner orchestration:

- main thread owns `ISystemBegin` platform/input collection and sequential `ISystemRender`;
- main thread increments a requested-frame generation after input publication;
- simulation thread observes the latest generation and collapses intermediate variable requests;
- simulation thread processes at most configured fixed catch-up ticks per iteration and preserves backlog;
- simulation thread executes one parallel `ISystemUpdate` batch for the latest request;
- shutdown and reload stop new publication, complete current update and fixed tick, then acknowledge quiescence;
- timeout triggers emergency worker restart without a fresh ECS snapshot.

Do not use `MainThreadScheduler` for mandatory per-frame update work. It remains a rare off-hot-path compatibility channel.

Audit existing `ISystemBegin` implementations while splitting ownership. Platform/input collection and graphics surface maintenance stay on main thread. Any begin system that depends on mutable simulation state must move to an explicit simulation-owned phase or be rejected during migration.

### Milestone 4: Graphics triple buffering and sort keys

Extend existing Graphics.Core:

- add compact `sortKey` to `DrawCommand`;
- retain per-type SoA command arrays in `ThreadBuffer`;
- replace implicit two-buffer ownership with three render-command sets: writing, merge/reading, and last completed;
- preallocate sort scratch and perform no-GC ordering by pass, layer, material/texture, depth, and stable tie-break;
- keep `MergeThread`;
- publish completed backend command list atomically;
- let main thread submit the last completed list repeatedly when no newer list is ready;
- remove ordinary `WaitForCompletion()` from the main-thread submit path;
- retain explicit waits for shutdown, resize ownership transitions where unavoidable, and reload.

Track backend command-list lifetime through submit and GPU use. Do not recycle a triple-buffer slot merely because CPU merge completed; recycle it only when the graphics backend contract allows reuse.

Frame buffer overflow fails fast with system ID, command type, configured capacity, and required capacity. Explicit resize is allowed only outside an active frame.

### Milestone 5: Client system migration

Move ECS-reading draw systems from `ISystemRender` to `ISystemRenderPrepare`:

- migrate `DrawSpriteSystem`;
- split or migrate `FlushDrawersSystem` so CPU command generation occurs during prepare and backend submit stays in Graphics.Core render systems;
- keep ImGui and swap buffers in sequential `ISystemRender`;
- ensure sample graphics output remains correct.

### Milestone 6: Shutdown, reload, and acceptance

Test:

- normal main-thread present never waits for simulation;
- delayed simulation repeats last completed frame;
- delayed merge repeats last completed frame;
- fixed backlog is bounded per iteration but not dropped;
- intermediate variable requests collapse;
- input overflow recovers;
- triple-buffer ownership never writes a set still read or submitted;
- client and server restart-worker hot reload quiesce jobs and buffers;
- at least five repeated reload cycles succeed.

## Concrete Steps

Commands run from `C:\Users\artem\RiderProjects\KarpikEngine`.

```powershell
dotnet test Modules\Client\Graphics\Graphics.Core.Tests\Graphics.Core.Tests.csproj -m:1 -nr:false
dotnet test Karpik.Engine.Core.Runner.Tests\Karpik.Engine.Core.Runner.Tests.csproj -m:1 -nr:false
dotnet build Modules\Client\Input\Input.csproj -m:1 -nr:false
dotnet build Modules\Client\Graphics\Graphics.Core\Graphics.Core.csproj -m:1 -nr:false
dotnet build ClientLauncher\ClientLauncher.csproj -m:1 -nr:false
dotnet build ServerLauncher\ServerLauncher.csproj -m:1 -nr:false
```

Run launcher builds sequentially.

## Validation And Acceptance

Accept when automated tests, allocation checks, delayed-simulation scenarios, input overflow recovery, and repeated reload smoke pass. Record any platform-specific manual observation in `Progress`.

## Idempotence And Recovery

Keep current sequential client-loop and Graphics.Core submission path selectable until threaded smoke passes. If triple-buffer ownership races, revert to the last passing fallback behind the same interface and retain the failing stress test.

## Artifacts And Notes

Create ADR `docs/02_ADR/client-threading-render-pipeline.md` before changing loop ownership.
