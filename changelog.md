# Unreleased

## Fixed
* ECS update registry generation now runs in `Modules` and `MyGame` assemblies, preventing runtime startup failures for registered systems whose descriptors were previously missing.
* `[SequentialSystem]` updates now execute on the scheduler's calling thread between completed parallel batches, keeping input, ImGui, networking, and other thread-affine compatibility systems off worker threads.

# v0.6

## Key Changes
* **Static runtime composition:** games ship as generated static hosts with no reflection — typed service factories, launch through your own launcher executable, manifest-free bundles. This is the default; the dynamic runtime stays as an opt-in rollback. Static hosts publish to NativeAOT.
* **External game SDK:** the engine ships as a versioned MSBuild SDK (`Karpik.Engine.Sdk`): the game lives in its own directory, builds with plain `dotnet restore/build/test/publish`, modules are selected in `Directory.Build.targets`. Two templates: `karpik-game` (minimal game) and `karpik-coinrush` (working multiplayer CoinRush sample). Project creation — from the launcher or `dotnet new`.
* **Constructor DI:** service registration is declarative (`[Export]` + `[ServiceRegistration]`), injection is constructor-only. Three scopes: `Engine -> ModSet -> Simulation`. Field injection (`[DI]`/`AutoInject`) is removed.
* **Content pipeline:** end-to-end pipeline — runtime contracts (`AssetRef`/`Lease`, `IContentStore`), `FileContentStore`, versioned `ContentRegistry` with `TryGet`/`LoadAsync`, Roslyn `ContentRefs` codegen from `[ContentType]`, target profiles (fonts, shaders), client and server content packaging.
* **Networking:** networking predates 0.6 (RPC codegen, LiteNetLib transport, `Network.Shared/Client/Server` modules). New in 0.6 is the generated typed snapshot registry `NetworkSnapshotRegistry` with a protocol schema hash: client and server with mismatched schemas do not connect.
* **Editor:** compact Unity-like workspace — dock layout presets and persistence, settings window, console with structured logs and filters, asset metadata editing.
* **Logging and lifecycle:** all engine diagnostics moved to `ILogger` with lossless shutdown; added an async system lifecycle.

## Static Composition and AOT
* The runtime is assembled by a composition generator: contracts, typed service factories, generated static hosts for client and server. No reflective module loading in static hosts.
* ECS registries (`IEcsUpdateRegistryProvider`) and service registrations are delivered through static composition; all ECS systems and cross-assembly services must be `public`.
* Static launcher publishing ships without a module manifest (`Content/`, `Mods/`, native dependencies only); the dynamic mode with a canonical manifest and universal runner is an opt-in rollback (`KarpikCompositionMode=Dynamic`, anything else fails with `KARPIK010`).
* NativeAOT: self-contained executable publishing (C++ workload required), roots for module assemblies, ECS component template instantiation, component deserialization without runtime generic instantiation, editor snapshots on source-generated JSON.
* AOT warnings: documented suppression inventory (aggregate IL2104/IL3053, IL3000/IL3002 from Silk.NET); any other trim/AOT warning is an error.
* Native libraries are laid out into the static launcher output; runtime build outputs are cached.

## External SDK and Templates
* `Karpik.Engine.Sdk`: versioned MSBuild SDK, game project validation (kinds `Runtime`/`Test`/`Tool`/`Generator`/`Assets`, sides `Client`/`Server`/`Shared`/`None`), static project graphs, late reference mutation rejection, runtime bundle construction, content pipeline integration.
* Two templates in the `Karpik.Engine.Templates` package: `karpik-game` — minimal game, `karpik-coinrush` — working multiplayer CoinRush sample (server-authoritative match, snapshots, physics, content, tests).
* Game creation: from the launcher (template and SDK selection) or `dotnet new karpik-game --name MyGame --karpik-sdk-version <version>`; the SDK version is pinned in `global.json` (`msbuild-sdks`), the launcher picks the editor matching the game's SDK.
* `MyGame` is moved onto the template, in-repo launchers are removed, the configurator is engine-only (no game paths).
* Engine payload: module dependency dedup into a shared store, stripped PDBs, five most recent SDK installations kept, full module catalog for SDK builds.

## Dependency Injection
* DI moved from field injection (`[DI]`/`AutoInject`, since 0.2) to constructors. Ordinary services register declaratively — both attributes at once:

```csharp
[Export(typeof(IFoo))]
[ServiceRegistration(ModuleScope.Simulation, ServiceLifetime.Singleton)]
public sealed class Foo(IClock clock) : IFoo { }
```

* Scopes form the chain `Engine -> ModSet -> Simulation`: a child sees parent services, not vice versa. `Singleton` means one instance per owning scope, `Transient` a new one per resolution. Do not lift mutable simulation state into a parent scope just to share it.
* `IModuleInstaller.OnRegisterServices` is only for factories, existing instances, conditional registrations, third-party types, and intentional overrides of attribute registration (installers run after attributes in the same scope).
* ECS systems are declared via `IModule.Add(ISystemRegistry)` with constructor dependencies resolved from the Simulation scope. Manually constructing registered services and service locators for statically known dependencies are forbidden; Autofac owns and disposes `IDisposable` by scope.
* For static hosts, registrations are turned into typed factories by the generator — no runtime reflection.

## Editor
* Unity-like workspace: compact shell, dock layout presets with persistence (density, selected preset, custom layouts), settings window.
* Editor console: structured log history, level filter (including Trace), runtime log filtering.
* Asset metadata editing in the editor.

## Content Pipeline
* Runtime: contracts (`AssetRef`/`Lease`, `IContentStore`, diagnostics), `FileContentStore` for loose files with root-escape protection, `ContentRegistry` with versioned slots, `TryGet`/`LoadAsync` and single-flight loading. `AssetRef` is blittable, copying is zero-alloc.
* Codegen: Roslyn `ContentRefs` generator with in-assembly `[ContentType]` mapping, `System.Text.Json 8.0.5`, KCO/KCR diagnostics.
* Target profiles: font and shader processors, client and server pipeline packaging, content metadata CLI, filesystem contract in engine core.
* SDK: module dependencies deduplicated into a shared store.
* JSON exponents (including extreme ones) are normalized canonically.

## Networking
* The foundation predates 0.6: RPC codegen (`RpcGenerator`, `TargetClientRpcGenerator`), LiteNetLib transport, `Network.Shared/Client/Server` modules.
* New: the snapshot generator builds a typed `NetworkSnapshotRegistry` with a `ProtocolSchemaHash` derived from snapshot component types; client and server configure the hash at startup and drop the handshake on schema mismatch.

## Logging and Lifecycle
* All engine diagnostics (Core host, IPC, runner, workers, jobs, networking) go through `ILogger` with call-site info; Trace filter in the editor. Process shutdown drains log queues — no logs are lost.
* Async system lifecycle; server init is idempotent on a restored world.
* ECS: component deserialization without runtime generic instantiation.

## Miscellaneous
* `Transform2D` moved to the new `Spatial2D` project; Kveldrid backend removed, moved to the `NeoVeldrid` NuGet.
* Safe texture resource disposal before graphics init.
* Threaded client: frame timing aggregation, present CPU time, async OpenGL GPU timings; hot reload builds skip unchanged native modules.

## Upgrade Notes
* Static composition is the default. Keep module systems and services `public`, with no hidden reflective registrations.
* New games come from templates (`karpik-game`, `karpik-coinrush`); the SDK version is pinned in `global.json`. Do not add manual `ProjectReference` items to the engine.
* Services: `[Export]` + `[ServiceRegistration]`, constructor injection only. Simulation state belongs to the Simulation scope, not above.
* Keep state that must survive Hot Reload in ECS components; recreate runtime resources.
* Content: use `AssetRef`/`Lease` and `IContentStore`; release leases.
* `ISystemFixedUpdate` stays sequential; physics and simulation run on fixed dt.
* AOT: fix new trim warnings instead of suppressing them.

# v0.5

## Key Changes
* **Native Memory Foundation:** Added `Karpik.Memory` with explicit unmanaged ownership, borrowed views, caller-owned result handles, fixed-capacity pools, linear allocation, and debug diagnostics for stale or invalid access.
* **No-GC Jobs Runtime:** Added a standalone value-job `JobScheduler` for `IJob` and `IJobFor` workloads with native descriptor storage, bounded worker queues, work stealing, dependency tracking, profiler hooks, and `0 B` managed allocation on measured steady-state paths after warm-up.
* **ECS Update Scheduler:** Replaced the planned `v0.4` sequential `ISystemUpdate` path with a generated, graph-based scheduler. `ISystemUpdate` is now parallel-by-default when component access is known and non-conflicting.
* **Scheduler Analyzer and Codegen:** Added static scheduler metadata, Roslyn validation, and a generated per-assembly update registry so unsafe or opaque update systems fail closed instead of running in parallel by accident.
* **Runner Integration:** Integrated the update scheduler into `EngineRunner` while preserving existing Dragon wrapper ordering and DI behavior. Production mode uses `JobScheduler` value jobs; deterministic and single-thread modes remain selectable.
* **Threaded Client Pipeline:** Added a headless/manual gameplay driver, main-thread/platform ownership, a dedicated simulation worker, bounded input publication, `ISystemRenderPrepare`, triple-buffered graphics command ownership, and non-blocking merge/submit.
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
* The threaded client simulation/render pipeline is part of the completed `v0.5` foundation. Main-thread platform/render work and simulation-thread gameplay/render preparation have explicit ownership boundaries.

# v0.4

## Key Changes
* **System Lifecycle:** Added an engine-owned deterministic pipeline: `Init -> Begin -> FixedUpdate -> Update -> LateUpdate -> Render -> Destroy`.
* **ECS API:** Added `DefaultWorld`, `EventWorld`, and `MetaWorld` facades, data-first component operations, and asynchronous lifecycle through `JobHandle`.
* **Hot Reload:** Moved reload to the restart-worker model. A new process restores ECS worlds, recreates runtime resources, and reconnects to the server correctly.
* **Module Graph:** Module dependencies now use a single `KarpikModuleDependency` item, are validated before startup, and expand into standard `ProjectReference` items for the IDE and compiler.
* **2D Rendering:** Replaced the Raylib backend with an OpenGL/Veldrid-like pipeline with command buffers, batching, camera support, textures, atlas fonts, and an ImGui overlay.
* **Physics2D:** Added a shared physics API, the `Physics2D.Aether2D` backend, ECS synchronization, and a platformer demo scene.

## Lifecycle and ECS
* Added `ISystemInit`, `ISystemBegin`, `ISystemFixedUpdate`, `ISystemUpdate`, `ISystemLateUpdate`, `ISystemRender`, and `ISystemDestroy`.
* `FixedUpdate` is used for physics and game simulation with fixed dt. Phases remain sequential in `v0.4`; scheduler-managed parallel execution is planned for `v0.5`.
* Added the preferred `DefaultWorld`, `EventWorld`, and `MetaWorld` facades.
* Added component add/remove operations with engine-owned lifecycle callbacks. Lifecycle errors include world, entity, component type, and phase diagnostics.
* Enabled `StaticAnalyzer` checks for raw Dragon lifecycle API usage and migration toward engine facades.
* Added tests for phase order, component lifecycle, and the ECS system dependency-graph prototype.

## Hot Reload
* Hot Reload now uses a separate worker process as the only reliable unload boundary for managed and native state.
* Only ECS state is persisted between workers. Services, graphics resources, sockets, and runtime handles are recreated after restart.
* Fixed client/server restoration: reconnect tokens, player entity rebinding, disconnected player cleanup, network cache restoration, and physics body recreation.
* Press `R` in the launcher console to reload either server or client manually. The client debug panel still exposes the `Hot Reload` button.

## Modules and Validation
* Projects under `Modules` and `MyGame` declare dependencies only through shorthand identifiers:

```xml
<KarpikModuleDependency Include="Physics2D" />
```

* Configurator derives side, plugin id, logical module id, and implementation from directory structure and `.csproj` names.
* Added structured `KarpikModuleSelection`, disabled/required dependency checks, cycle detection, side-leak validation, and stale generated-artifact checks.
* Added tracked `Generated/KarpikModuleCatalog.props`, allowing Rider and the compiler to resolve project references after reload.
* Installer initialization order is deterministic and teardown runs in reverse order.
* After adding, removing, or moving a project, run:

```powershell
dotnet run --project Configurator/Configurator.csproj -- --generate
dotnet run --project Configurator/Configurator.csproj -- --validate
```

## Graphics, Window, and Input
* Removed the `Graphics.Raylib` backend. Added `Graphics.OpenGL`, `Window.Core`, and `Window.Sdl2`.
* Added a 2D command-buffer renderer: `DrawRect`, `DrawTexture`, `DrawText`, batching, and a merge thread.
* Added `Camera2D`, camera-relative rendering, rotation, and configurable draw origin.
* Added texture, shader, and atlas SDF font loading through AssetManagement.
* Added an ImGui overlay for runtime diagnostics.
* Moved input handling to the SDL2 window/input source.

## Physics2D and Game Sample
* Added `Physics2D.Core` and the `Physics2D.Aether2D` implementation.
* Added ECS components for bodies, shapes, transforms, velocities, and collision layers.
* Added body creation, restoration, push/pull synchronization, and physics-step systems.
* Expanded the sample game into a platformer scene with platforms, player movement, a kinematic controller, and server-side input handling.

## Upgrade Notes
* User-facing ECS systems should migrate from Dragon `IEcs*` lifecycle APIs to the engine `ISystem*` interfaces.
* Do not add direct `ProjectReference` items under `Modules` or `MyGame`; use `KarpikModuleDependency`.
* Keep gameplay state that must survive Hot Reload in ECS components. Recreate process-local handles and runtime resources.
* The `v0.3` UI Toolkit has been removed from the runtime. A new UI API will be designed separately.


# v0.3

## Key Changes
* **Assets:** Added a system for loading and unloading resources.
* **Main Thread Execution:** Added MainThreadScheduler to execute logic in the main thread, for example, for working with graphics.
* **Project Architecture:** The project is divided into modules and game elements. There is no fundamental difference, except that MyGame.**Side**.Main implies using all available modules and subprojects of MyGame. While Modules reference only the other modules they need.
* **Karpik.Jobs:** Implemented throughout the project.
* **Hot Reload:** Added support for hot reload. Full support. Without limitations of standard .NET.

## Assets
* Added AssetManagement module. Used for unified loading and unloading of assets (textures, configs).
* Usage can be seen in SpriteRenderer and DemoModuleClient.

## Main Thread Execution
* Added MainThreadScheduler class. Used for executing actions in the main thread.

## Project Architecture
* The project is divided into Modules and Game. Each of them is split into Client, Server, and Shared.
* Modules imply generalized logic, not dependent on a specific game. For example, AssetManagement, which is used in every game.
* Modules can react to different events, see IModule.cs in Karpik.Engine.Core.Runner project for details. ModuleAttribute and IModule are mandatory.
* For Game projects, the behavior is similar.
* If you add a new Module or Game project, run the command `dotnet run --project Configurator/Configurator.csproj -- --generate`. It will automatically generate files based on all projects.

## Hot Reload
* Oh yes, this took the most time.
* Currently, a separate process is used for Hot Reload, into which all libraries are loaded.
* For correct operation in Debug mode, enable automatic debugger attachment to child processes in your IDE settings.
* How it works: You start the game, the game logic doesn't work correctly, you change the logic, you trigger Build project, **check if files in the modules directory were updated (usually you need to trigger Build 2 times)**, click on `Hot Reload` in the debug panel.
* By default, all entities in each ECS world are preserved, so it's recommended to store all data in ECS worlds. Also, you can configure your own saving variation.



# v0.2

## Key Changes
* **UI:** Added an HTML+CSS inspired UI system. See Wiki for details.
* **Dependency Injection:** Most static classes were removed and replaced with Dependency Injection. Implemented `AutoInject` for systems.
* **Karpik.Jobs:** Imported and integrated the job system.
* **Tween:** Added tweens (GTweens).

## Architecture and ECS
* Added `EcsCommandBuffer`, `BaseSystem`, and `IEcsRunParallel`.
* Renamed projects and added the `Game.targets` file.

## Network and Modding
* **Network:**
    * Fixed RPC behavior.
    * Added `LocalPlayer` component to identify the local player.
    * Implemented automatic port selection for the client.
* **Modding:**
    * Added separation into client and server subfolders for mods.
    * Added links to `Mods` and `Content` folders in the Debug build.
    * Fixed delta time (dt) calculation on the server.
