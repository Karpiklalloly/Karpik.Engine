# Separate headless gameplay loop and client render ownership

This ExecPlan is a living document. It must be maintained according to `plans/PLANS.md`.

## Purpose / Big Picture

Make the gameplay loop runnable independently from the client graphics/platform loop, especially from automated tests. A headless/manual gameplay driver must be able to tick fixed update, scheduled `ISystemUpdate`, and gameplay-owned phases without creating a window, swapchain, ImGui context, or Graphics.Core backend.

The threaded client is one runtime mode built on top of that boundary: the client can run gameplay on a dedicated simulation thread while keeping platform events, input collection, graphics submit, ImGui, and present on the OS/main thread. Add demand-driven read-only parallel `ISystemRenderPrepare` and extend the existing Graphics.Core command pipeline with sort keys and triple-buffer ownership.

The renderer repeats the last completed frame when simulation or merge is late. Normal present does not wait for simulation.

## Progress

- [x] (2026-06-03 00:33 +04:00) Design agreed as child plan 4 of `plans/scheduler-jobs-memory-execplan.md`.
- [x] (2026-06-19 21:40 +04:00) Recorded the current baseline: `EngineRunner.Run` executes begin, fixed, scheduled update, late, and render serially on its caller; Input mutates concurrent dictionaries and per-frame lists from `ISystemBegin`; Graphics.Core swaps global write/pending lists under locks, `BeginMerge` waits for an unfinished prior merge, and scene submit waits for merge completion; restart-worker reload destroys and reconstructs the runner/pipeline around ECS-only persisted module state.
- [x] (2026-06-19 22:00 +04:00) Completed Milestone 1: added `ISystemRenderPrepare`, a dedicated generated registry and scheduler graph, Builder/Runner lifecycle integration, read-only analyzer enforcement (`K009`), and a neutral `[RenderPrepareCommand]` contract that rejects command emission from `ISystemUpdate` (`K010`). Static analyzer tests passed 43/43, runner tests passed 12/12, Graphics.Core built, and both launcher builds passed; existing package/nullability warnings remain.
- [x] (2026-07-06 00:00 +04:00) Replaced `Input` internals with enum-indexed key/mouse state, transient edge arrays, a bounded SPSC `InputEvent` ring, consume-phase `KeyPressed`/`KeyReleased`/`TextInput` callbacks, legacy event compatibility, and request-based cursor locking. Added focused `Modules/Client/Input.Tests`; input tests passed 6/6, Input built, and ClientLauncher built. Existing NuGet feed/nullability warnings remain.
- [x] (2026-07-07 00:00 +04:00) Added `GameplayLoopDriver` and `EngineRunner.CreateGameplayLoopDriver()` for manual/headless gameplay stepping through existing Runner setup. The driver can step all non-render runtime cycles: begin, ordinary ECS run, fixed update, scheduled update, and late update; `RenderPrepare` and `Render` remain excluded. Added runner tests for fixed tick count, individual begin/run/update steps, full non-render frame order, render/render-prepare exclusion, and negative tick rejection. Runner tests passed 18/18, ClientLauncher built, and ServerLauncher built. Existing package/nullability warnings remain.
- [x] (2026-07-08 00:00 +04:00) Added `Window.Headless` as a peer implementation next to `Window.Sdl2`. It registers `IWindow`, `IInputSource`, and `HeadlessInputController` for deterministic no-window input tests without changing `InputInstaller` or ordinary ClientLauncher module staging. Added an integration test that drives `Input` through `WindowCoreInstaller + WindowHeadlessInstaller + InputInstaller`; Input tests passed 7/7 and Window.Headless built. Restore/build emitted existing offline NuGet vulnerability-feed warnings.
- [x] (2026-07-09 00:00 +04:00) Introduced the first Graphics backend boundary: `Graphics.Core` lifecycle systems now depend on backend-neutral `IGraphicsBackend`, and `Graphics.OpenGL` registers `OpenGLGraphicsBackend` to own Veldrid frame begin, merge, scene submit, ImGui render, and swap buffers. Added a contract test that prevents Veldrid types from leaking through `IGraphicsBackend`; Graphics.Core tests passed 29/29, Graphics.Core built, and Graphics.OpenGL built. Existing package vulnerability/pruning warnings remain.
- [x] (2026-07-09 00:00 +04:00) Added `Graphics.Headless` as a peer backend module. It registers a no-op `IGraphicsBackend`, begins a `GraphicsContext` frame without GPU objects, reports no ImGui input capture, and does not submit/present. Moved ImGui update/capture behind `IGraphicsBackend` so headless graphics does not need `ImGuiRenderContext`. Graphics.Core tests passed 29/29, Graphics.Headless built, and ClientLauncher built with the ordinary OpenGL staging path unchanged. Existing offline NuGet vulnerability-feed warnings remain.
- [x] (2026-07-09 00:00 +04:00) Migrated sample sprite command generation from render to render-prepare: `DrawSpriteSystem` now implements `ISystemRenderPrepare`, `FlushDrawersSystem` flushes `Drawer` during render-prepare, and `Drawer` is explicitly marked as a render-prepare command writer. Removed the per-sprite debug position text because it allocated formatted strings in the frame path. ClientLauncher built successfully; existing nullability/NuGet warnings remain.
- [x] (2026-07-09 00:00 +04:00) Added sort keys to Graphics.Core command recording. `DrawRectCmd`, `DrawTextureCmd`, and `DrawTextCmd` now carry `SortKey`; `ThreadBuffer` stores command descriptors with stable sequence numbers and returns them ordered by `(SortKey, Sequence)` without moving payload arrays. Added `DrawSortKey` helpers and switched sample `Drawer` layer ordering to command sort keys instead of a separate action-array sort. Graphics.Core tests passed 34/34, Graphics.Core built, Graphics.OpenGL built, and ClientLauncher built. Existing warnings remain.
- [x] (2026-07-09 00:00 +04:00) Replaced implicit double-buffer `GraphicsContext` ownership with explicit three command-set slots and three thread-local command buffers while preserving the current synchronous merge/submit API. Added command-set ownership tests; Graphics.Core tests passed 36/36, Graphics.Core built, Graphics.OpenGL built, and ClientLauncher built. Existing warnings remain.
- [x] (2026-07-09 00:00 +04:00) Split `MergeThread` start and completed-frame consumption into non-blocking APIs. `OpenGLGraphicsBackend.BeginMerge()` now skips starting a new merge while the worker is busy, and `SubmitScene()` submits the last completed command list if one exists instead of waiting for the current merge. Graphics.Core tests passed 36/36, Graphics.Core built, Graphics.OpenGL built, and ClientLauncher built. Existing warnings remain.
- [x] (2026-07-09 00:00 +04:00) Added GPU-fence-aware merge context reuse. `MergeThread` now owns three Veldrid merge contexts, each with a submit fence, and only builds into a context whose fence is signaled and is not the current completed frame. `OpenGLGraphicsBackend` submits completed command lists with their fence and skips submit while the previous submission of that context is still in flight. Graphics.Core tests passed 36/36, Graphics.Core built, Graphics.OpenGL built, and ClientLauncher built. Existing warnings remain.
- [x] (2026-07-09 00:00 +04:00) Moved the Veldrid merge-thread contract and implementation (`IMergeThread`, `MergeThread`, `MergeContext`) from `Graphics.Core` into `Graphics.OpenGL`. The OpenGL backend now owns the complete command-list/fence lifetime boundary; shared Core retains only backend-neutral lifecycle and command data plus the currently shared vertex layout. Used a friend-assembly boundary for Core-private command representation rather than publishing it. Graphics.Core tests passed 36/36; Graphics.Core, Graphics.OpenGL, and ClientLauncher built successfully. Existing NuGet/nullability warnings remain.
- [x] (2026-07-10 00:00 +04:00) Added the first client-loop ownership boundary: `ISystemMainThreadBegin` has its own runner and executes before simulation-owned `ISystemBegin`. Moved Window, Graphics frame/merge, and ImGui begin systems to main-thread ownership. Split Input publication (`PublishPlatformFrame`) from simulation consumption/callback delivery (`ConsumeSimulationFrame`); legacy `Input.Update` keeps the synchronous combined path. Added `EngineRunner.RunMainThreadBegin()` and updated the headless input integration test to drive platform then simulation phases. Runner tests passed 18/18, Input tests passed 7/7, and ClientLauncher built successfully. Existing warnings remain.
- [x] (2026-07-10 00:00 +04:00) Made `InputEventRing` a real bounded SPSC ring. Producer and consumer now own separate monotonic positions and publish them with `Volatile` acquire/release operations; the fixed power-of-two capacity keeps hot-path slot lookup to a mask and removes the concurrently-mutated count. Overflow-prefix ordering remains covered by the existing ring test. Input built and Input tests passed 7/7. Existing package warnings remain.
- [x] (2026-07-10 00:00 +04:00) Made Input's latest-state publication safe for independently progressing platform and simulation threads. Held key/button state, mouse capture, and mouse coordinate components are published/read atomically; overflow recovery reads the latest held values without sharing mutable collection state. Cursor requests now cross from simulation to the platform source atomically. Input tests passed 7/7. Existing package warnings remain.
- [x] (2026-07-10 00:00 +04:00) Enabled the threaded client runtime. Main thread executes platform begin every present, starts a new frame begin/merge only when a dedicated simulation worker has completed the prior gameplay/render-prepare frame, and always renders/presents without waiting for simulation. The worker blocks only while idle, publishes its fault/shutdown state back to main, and is joined before engine teardown. Time values now publish atomically for main-thread ImGui reads. Removed the legacy `MySystem` entity-inspector UI from ordinary module registration because it called ImGui and mutated/read ECS from the same update; retained Escape pause as a sequential simulation system. Runner tests passed 18/18; MyGame.Client.Main and ClientLauncher built successfully. Existing warnings remain.
- [x] (2026-07-10 00:00 +04:00) Added isolated worker acceptance tests through an internal gameplay-loop contract. They verify that main-side reservation does not wait for an active simulation frame, shutdown waits for the active frame before joining, and worker exceptions are published to the caller. Runner tests passed 21/21.
- [x] (2026-07-10 00:00 +04:00) Moved the restart-worker executable (`Karpik.Engine.Core.Runner.Program.ClientLoop`) onto the same split client loop as `CoreRunner`: platform/main-thread phases and present stay on its main thread; gameplay, fixed/update, and render preparation run through `ClientSimulationWorker`. Runner tests passed 21/21 and `Karpik.Engine.Core.Runner` built. Manual Debug/hot-reload smoke remains required because that executable owns the live SDL/OpenGL path.
- [x] (2026-07-11 00:00 +04:00) Added lock-free client frame telemetry. `Bootstrap` owns a `ClientFrameMetrics` service registered into DI; runner, simulation worker, and OpenGL merge worker publish timestamp deltas through volatile fields, while the ImGui debug panel reads a snapshot. The panel reports main-thread frame/begin/render, simulation queue and execution, merge build duration, and command-list availability. Added snapshot regression coverage; runner tests passed and ClientLauncher compiled. Existing warnings remain.
- [x] (2026-07-11 00:00 +04:00) Added a controllable graphics load probe to the ImGui panel. It publishes a volatile quad count and a render-prepare system emits up to 8192 screen-space rectangle commands without creating ECS entities, touching network state, or allocating during steady-state frames. The panel offers slider and 1k/4k/8k presets. Graphics.Core tests passed 37/37 and ClientLauncher built. Existing warnings remain.
- [x] Split client platform/render loop from gameplay loop and add the threaded client runtime mode.
- [ ] Extend Graphics.Core with sorted triple-buffer command-set ownership.
- [x] Migrate client drawing systems from `ISystemRender` to `ISystemRenderPrepare`.
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

- Observation: The current client lifecycle has no ownership boundary between platform, simulation, and presentation phases; `EngineRunner.Run` invokes all phases synchronously on one caller.
  Evidence: `Karpik.Engine.Core.Runner/Runner.cs` calls begin, pipeline, fixed, scheduled update, late, and render in one method.

- Observation: The original "dedicated simulation thread" wording hides the primary testability requirement. The required boundary is that gameplay can run headlessly and manually from tests; a separate client simulation thread is only one production runtime mode over that boundary.
  Evidence: developer clarification on 2026-07-02.

- Observation: Ordinary scene submit blocks on merge completion, and starting a new merge also blocks if the previous merge is unfinished.
  Evidence: `GraphicsCoreSubmitSceneSystem.Render()` and `MergeThread.BeginMerge()` call `WaitForCompletion()`.

- Observation: The scheduler analyzer cannot reference Graphics.Core without violating the Core/Client dependency boundary.
  Evidence: scheduler contracts and analyzers depend on Core metadata, while `GraphicsContext` is defined under `Modules/Client/Graphics`.

- Observation: Attribute-based command emission contracts must support interfaces because render systems write through `ICommandBuffer`, not a concrete Graphics.Core buffer type.
  Evidence: the first Graphics.Core validation failed with `CS0592` until `RenderPrepareCommandAttribute` included `AttributeTargets.Interface`; the regression test now locks this contract.

- Observation: `IInputSource` does not expose a dense "all keys currently held" list; held keyboard state is reconstructed from key press/release edges inside `Input`.
  Evidence: `IInputSource` exposes `PressedKeys`, `UnPressedKeys`, and repeat lists, while `Input` now owns `_publishedKeyDown` and `_keyDown`.

- Observation: Launcher builds should be run sequentially in this workspace. Running ClientLauncher and ServerLauncher builds at the same time can lock generated assemblies.
  Evidence: a parallel validation attempt failed ClientLauncher with `CS2012` on `Network.Codegen.dll`; rerunning ClientLauncher sequentially passed.

- Observation: Headless window/input should not be added to ordinary `PluginReference` staging while `Window.Sdl2` remains the default client backend.
  Evidence: both implementations register `IWindow` and `IInputSource`; staging both for ClientLauncher would make backend selection depend on service registration order.

- Observation: `Graphics.Core` still contains Veldrid-dependent resource and merge implementation types after the first backend boundary.
  Evidence: `MergeThread`, `TextureLoader`, `Preset2DPipeline`, `ImGuiRenderContext`, and `Graphics.Core.csproj` still reference Veldrid packages. True `Graphics.Headless` requires moving or abstracting these pieces behind backend-specific contracts.

- Observation: ImGui frame update now goes through `IGraphicsBackend`, but debug panel drawing still lives in Graphics.Core and uses ImGuiNET directly.
  Evidence: `ImGuiBeginSystem` calls `IGraphicsBackend.UpdateImGui`, while `ImGuiDebugPanelSystem` still uses `ImGui.Begin/Text/InputText/End`.

- Observation: `Graphics.Headless` can now run the Graphics.Core lifecycle without GPU resources, but Graphics.Core itself is not yet dependency-clean.
  Evidence: `Graphics.Headless` builds and registers a no-op `IGraphicsBackend`, while `Graphics.Core.csproj` still references Veldrid packages because command/resource implementation types remain there.

- Observation: The sample client has an `ISystemUpdate` (`MySystem`) that calls ImGui and directly mutates ECS from UI button callbacks. Running it on simulation while ImGui is owned by the main thread would race both ImGui and the ECS world.
  Evidence: `MyGame/Client/MyGame.Client.Main/DemoModuleClient.cs` calls `ImGui.*`, `Spawn`, and `_world.Del` from `MySystem.Update()`.

- Observation: `Drawer` cannot use `Array.Resize`, `Span.Sort`, or interpolated debug text inside render preparation.
  Evidence: the scheduler analyzer reported opaque helper calls after migrating `FlushDrawersSystem` to `ISystemRenderPrepare`; the code now uses fixed capacity, insertion sort, and no per-sprite debug string formatting.

- Observation: Sorting command descriptors is enough for the current merge path; rect/texture/text payload arrays do not need to move.
  Evidence: `DrawCommand` stores command type plus payload index, and `MergeThread` resolves payloads through `rects[command.Index]`, `textures[command.Index]`, or `texts[command.Index]`.

- Observation: OpenGL scene submit is now non-blocking with respect to the current CPU merge, but explicit waits remain available for shutdown/reload-style barriers.
  Evidence: `OpenGLGraphicsBackend.SubmitScene()` uses `TryGetCompletedCommandList()`, while `MergeThread.WaitForCompletion()` remains on `IMergeThread`.

- Observation: Backend command-list lifetime is now protected by Veldrid fences, but the fence contract still leaks through `IMergeThread`.
  Evidence: `MergeContext` stores `SubmitFence`, `TryGetCompletedCommandList` returns a `Fence`, and `OpenGLGraphicsBackend.SubmitScene()` submits with that fence.

## Decision Log

- Decision: Main thread owns platform events, input publication, backend submit, ImGui, and present. Simulation thread owns fixed ticks, parallel update, and render preparation.
  Rationale: Graphics backend and platform affinity remain explicit while gameplay work scales independently.
  Date/Author: 2026-06-03 / developer and agent

- Decision: Split the core requirement into a headless/manual gameplay driver plus a threaded client runtime mode.
  Rationale: Tests must be able to run gameplay ticks without graphics, platform events, a window, swapchain, ImGui, or GPU resources. The production client can then place that gameplay driver on a dedicated simulation thread without making threading the only way to exercise gameplay.
  Date/Author: 2026-07-02 / developer and agent

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

- Decision: Keep `Input` as the gameplay-facing DI service while changing its internals to publish/consume state.
  Rationale: Existing client gameplay systems can keep using `[DI] Input`, while public getters stop reading `IInputSource` directly and become safe to separate from platform ownership later.
  Date/Author: 2026-07-06 / developer and agent

- Decision: Official input callbacks are `KeyPressed`, `KeyReleased`, and `TextInput`, invoked from the consume phase after draining the bounded event ring.
  Rationale: Subscription remains supported, but callbacks observe the same frame state as polling APIs instead of firing directly from platform event publication.
  Date/Author: 2026-07-06 / developer and agent

- Decision: The manual gameplay driver is created from an already set up `EngineRunner` and reuses the existing `Time`, fixed runner, scheduled update runner, and late runner.
  Rationale: Tests exercise the same Builder/Runner lifecycle as production without duplicating module setup or leaking Client graphics/platform dependencies into Core.
  Date/Author: 2026-07-07 / agent

- Decision: `GameplayLoopDriver` steps fixed, scheduled update, and late phases only; begin/platform collection and render/presentation stay outside this headless gameplay boundary.
  Rationale: The driver must be usable by tests without a window, swapchain, ImGui context, Graphics.Core backend, or platform input source. Client threading can later compose platform begin/render around this gameplay driver.
  Date/Author: 2026-07-07 / agent

- Decision: `GameplayLoopDriver.StepFrame` follows `EngineRunner.Run` ordering for every non-render cycle: time update, begin, ordinary ECS run, fixed update backlog, scheduled update, and late update. `RenderPrepare` and `Render` are deliberately not reachable through the gameplay driver.
  Rationale: Headless tests need the same gameplay-side ordering as production while keeping render preparation and presentation outside the manual gameplay boundary.
  Date/Author: 2026-07-07 / developer and agent

- Decision: Add `Window.Headless` as a full peer module implementation of `Window.Core` contracts, not as a test-only fake inside `Input.Tests`.
  Rationale: Integration tests can exercise real DI/module wiring through `IWindow` and `IInputSource` without SDL, OS windows, swapchains, or ClientLauncher backend changes.
  Date/Author: 2026-07-08 / developer and agent

- Decision: `Graphics.Core` owns a backend-neutral lifecycle contract, while backend modules own concrete graphics device, swapchain, command-list submit, ImGui render, and present behavior.
  Rationale: Headless graphics should not fake Veldrid concrete objects. The current OpenGL backend keeps the existing Veldrid path intact while creating a seam where a future `Graphics.Headless` module can register no-op/frame-recording behavior.
  Date/Author: 2026-07-09 / developer and agent

- Decision: Add `Graphics.Headless` as a peer backend module and keep it out of ordinary ClientLauncher plugin staging while `Graphics.OpenGL` is the default client backend.
  Rationale: Tests and headless scenarios can opt into no-GPU graphics explicitly without making production backend selection depend on service registration order.
  Date/Author: 2026-07-09 / agent

- Decision: Mark `Drawer` as a render-prepare command writer and keep sprite command generation out of `ISystemRender`.
  Rationale: `Drawer` is part of the graphics command publication path, not gameplay simulation. Marking it with the command contract makes accidental `Update` usage fail analyzer validation while allowing render-prepare systems to publish draw commands.
  Date/Author: 2026-07-09 / agent

- Decision: Store sort order on command descriptors, not by rearranging command payload arrays.
  Rationale: Payload arrays remain dense and type-specific for merge locality; the ordered descriptor stream can be sorted independently and still reference the original payload index.
  Date/Author: 2026-07-09 / agent

- Decision: Introduce explicit triple command-set slots before removing merge waits.
  Rationale: Separating writing/pending/reusable ownership first keeps the behavioral change small and gives tests a stable boundary before changing `MergeThread` publication and main-thread submit semantics.
  Date/Author: 2026-07-09 / agent

- Decision: Make ordinary OpenGL submit opportunistic: submit the last completed command list when available and skip scene submit before the first merge completes.
  Rationale: The main render path must not wait for CPU merge. Repeating the last completed frame is acceptable; first-frame no-op is preferable to blocking.
  Date/Author: 2026-07-09 / agent

- Decision: Use three Veldrid merge contexts with per-context submit fences before broader backend extraction.
  Rationale: This prevents CPU merge from rebuilding a command list context that may still be in GPU use, while keeping the current Veldrid implementation path intact for a small, verifiable change.
  Date/Author: 2026-07-09 / agent

- Decision: Represent render-prepare-safe command emission with a neutral Core metadata attribute applied by Graphics.Core, rather than adding a Core or analyzer dependency on the Client graphics module.
  Rationale: The analyzer can recognize the contract by metadata name while Client-only graphics types remain outside Shared/Core dependencies.
  Date/Author: 2026-06-19 / agent

## Outcomes & Retrospective

No outcome yet.

## Context And Orientation

Lifecycle:

- `Karpik.Engine.Core/LifeCycle/IBuilder.cs`
- create `Karpik.Engine.Core/LifeCycle/ISystemRenderPrepare.cs`
- `Karpik.Engine.Core.Runner/Builder.cs`
- `Karpik.Engine.Core.Runner/Runner.cs`
- create or expose a headless/manual gameplay loop driver under `Karpik.Engine.Core.Runner/`
- `Karpik.Engine.Core/CoreRunner.cs`
- `Karpik.Engine.Core.Runner.Tests`

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

This plan changes gameplay loop ownership, test runner boundaries, client frame loops, input update, ECS presentation reads, graphics command recording, sort, merge, submit, and reload barriers.

Required `0 B/frame` managed allocation after warm-up:

- headless/manual gameplay stepping in tests after setup;
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
headless/manual test driver
  caller controls dt, fixed tick count, and update requests
  run bounded sequential fixed backlog
  run scheduled ISystemUpdate graph
  optionally run non-graphics gameplay phases needed by the test
  no window, swapchain, ImGui, Graphics.Core backend, or wall-clock dependency

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

Implement the gameplay/client boundary in seven reviewable milestones. First make gameplay independently drivable for tests, then layer the threaded client runtime and Graphics.Core ownership changes on top. Keep the current sequential client loop and Graphics.Core submission path selectable until the final smoke gate passes.

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

### Milestone 3: Headless/manual gameplay driver

Expose a testable gameplay loop driver that can run without Client graphics/platform modules:

- initialize the required world, scheduler, and lifecycle state through existing Builder/Runner paths;
- let tests control fixed tick count, fixed dt, variable update requests, and scheduler mode;
- run fixed update and scheduled `ISystemUpdate` without creating a window, swapchain, ImGui context, Graphics.Core backend, or real platform input source;
- support no-op or test adapters for phases that are not part of gameplay;
- expose deterministic single-thread mode for assertions and production scheduler mode for integration/allocation tests.

Add runner tests that create a small gameplay system set, step N fixed ticks and updates manually, and assert component state without any graphics module dependency.

### Milestone 4: Client loop split and threaded runtime mode

Refactor `CoreRunner.ClientLoop` and runner orchestration:

- main thread owns `ISystemBegin` platform/input collection and sequential `ISystemRender`;
- client simulation thread owns the gameplay driver in production threaded mode;
- main thread increments a requested-frame generation after input publication;
- simulation thread observes the latest generation and collapses intermediate variable requests;
- simulation thread processes at most configured fixed catch-up ticks per iteration and preserves backlog;
- simulation thread executes one parallel `ISystemUpdate` batch for the latest request;
- shutdown and reload stop new publication, complete current update and fixed tick, then acknowledge quiescence;
- timeout triggers emergency worker restart without a fresh ECS snapshot.

Do not use `MainThreadScheduler` for mandatory per-frame update work. It remains a rare off-hot-path compatibility channel.

Audit existing `ISystemBegin` implementations while splitting ownership. Platform/input collection and graphics surface maintenance stay on main thread. Any begin system that depends on mutable simulation state must move to an explicit simulation-owned phase or be rejected during migration.

### Milestone 5: Graphics triple buffering and sort keys

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

### Milestone 6: Client system migration

Move ECS-reading draw systems from `ISystemRender` to `ISystemRenderPrepare`:

- migrate `DrawSpriteSystem`;
- split or migrate `FlushDrawersSystem` so CPU command generation occurs during prepare and backend submit stays in Graphics.Core render systems;
- keep ImGui and swap buffers in sequential `ISystemRender`;
- ensure sample graphics output remains correct.

### Milestone 7: Shutdown, reload, and acceptance

Test:

- headless gameplay tests run without graphics/platform dependencies;
- manual stepping controls fixed dt and tick count deterministically;
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

Accept when headless gameplay runner tests, automated graphics tests, allocation checks, delayed-simulation scenarios, input overflow recovery, and repeated reload smoke pass. Record any platform-specific manual observation in `Progress`.

## Idempotence And Recovery

Keep current sequential client-loop and Graphics.Core submission path selectable until headless gameplay tests and threaded smoke pass. If triple-buffer ownership races, revert to the last passing fallback behind the same interface and retain the failing stress test.

## Artifacts And Notes

Create ADR `docs/02_ADR/client-threading-render-pipeline.md` before changing loop ownership.
