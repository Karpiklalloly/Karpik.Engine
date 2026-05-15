# Restart-worker hot reload

This ExecPlan is a living document. It must be maintained according to `plans/PLANS.md`.

## Purpose / Big Picture

Replace the mixed in-process/collectible-context hot reload path with a reliable restart-worker flow. A launcher/watcher process stays alive, the game worker process owns all runtime module/native state, and hot reload restarts that worker after capturing ECS world state.

A developer can see this working by building `ClientLauncher` or `ServerLauncher`, starting the launcher, pressing the existing Hot Reload button, and observing a new worker PID with ECS entities restored.

## Progress

- [x] (2026-05-15 19:37:57 +04:00) Initial plan created.
- [x] (2026-05-15 19:47:00 +04:00) Implemented restart-worker default options and launcher flow.
- [x] (2026-05-15 19:47:00 +04:00) Fixed worker side-specific module loading.
- [x] (2026-05-15 19:47:00 +04:00) Simplified module shadow loading for process-owned reload.
- [x] (2026-05-15 19:47:00 +04:00) Restricted persisted reload state to ECS.
- [x] (2026-05-15 19:47:00 +04:00) Tightened module deploy diagnostics.
- [x] (2026-05-15 20:22:42 +04:00) Validate client and server builds.
- [x] (2026-05-15 19:48:00 +04:00) `dotnet build Karpik.Engine.Core/Karpik.Engine.Core.csproj --no-restore` succeeded with 0 warnings and 0 errors.
- [x] (2026-05-15 20:22:42 +04:00) `dotnet build ClientLauncher/ClientLauncher.csproj -m:1 /nr:false` succeeded with 18 warnings and 0 errors.
- [x] (2026-05-15 20:22:42 +04:00) `dotnet build ServerLauncher/ServerLauncher.csproj -m:1 /nr:false` succeeded with 7 warnings and 0 errors.
- [x] (2026-05-15 20:30:00 +04:00) Fixed runtime `AssemblyLoadContext is unloading or was already unloaded` failure by making `PluginLoadContext` non-collectible and keeping an explicit loader field reference.

## Surprises & Discoveries

- Observation: `Karpik.Engine.Core.Runner.Program` currently always calls `LoadClientModules()`, even for `Side.Server`.
  Evidence: `Karpik.Engine.Core.Runner/Program.cs`.
- Observation: `Generated/ModuleLoader.cs` still contains ALC unload/static-field cleanup logic, even though process restart is the intended reliable unload boundary.
  Evidence: `Generated/ModuleLoader.cs`.
- Observation: Launcher validation was initially blocked before compilation by accumulated MSBuild worker node processes.
  Evidence: `dotnet build` exited 1 with 0 warnings and 0 errors, and `Get-CimInstance Win32_Process` showed many `dotnet.exe ... MSBuild.dll /nodemode:1 /nodeReuse:true` processes. After stopping 130 stale MSBuild nodes and rebuilding with `/nr:false`, the launchers built successfully.
- Observation: The first fixed build exposed a `Plugins.targets` staging bug.
  Evidence: MSBuild attempted to copy literal paths ending in `bin\Debug\net10.0\**\*.*`; moving the wildcard into batched item include `%(_PluginOutputRoots.Identity)**\*.*` fixed module staging.
- Observation: The worker still used a collectible plugin load context after switching to process restart.
  Evidence: Client startup failed loading `Veldrid.StartupUtilities` from `WindowSdlInstaller.OnRegisterServices` with `AssemblyLoadContext is unloading or was already unloaded`; `PluginLoadContext` used `base(isCollectible: true)`. The dependency was present in staged `modules`, so the failure was context lifecycle, not deployment.

## Decision Log

- Decision: Hot reload v1 uses restart-worker as the only true reload mechanism.
  Rationale: It reliably releases managed assemblies, static state, event subscriptions, background tasks, and native libraries by terminating the worker process.
  Date/Author: 2026-05-15 / Codex
- Decision: Persist only ECS state.
  Rationale: Gameplay state must live in ECS; module services, graphics resources, network sockets, mod runtimes, and static caches are disposable runtime resources.
  Date/Author: 2026-05-15 / Codex
- Decision: Keep manual build and `modules/` staging.
  Rationale: MSBuild/Rider remain the source of truth for project references, analyzers, generated code, conditions, and runtime assets.
  Date/Author: 2026-05-15 / Codex

## Outcomes & Retrospective

Implemented the restart-worker hot reload code path and verified `Karpik.Engine.Core`, `ClientLauncher`, and `ServerLauncher` build. Client module staging includes `ECS.Core`, `Graphics.Core`, and `Network.Client.Core`; server staging includes `ECS.Core` and `Network.Server.Core` and does not include `Network.Client.Core`.

## Context and Orientation

`ClientLauncher` and `ServerLauncher` call `CoreRunner.Start(...)`. `CoreRunner` currently has a `SUPER_HOT_RELOAD` compile-time path for process isolation and a direct in-process path otherwise.

`ProcessManager` starts `Karpik.Engine.Core.Runner.exe`, hosts `IpcServer`, requests `HotReloadState` from the worker, sends shutdown, and starts a new worker with serialized state.

`Karpik.Engine.Core.Runner.Program` connects to the watcher through `IpcClient`, loads generated `ModuleLoader` modules, initializes `Bootstrap`, and responds to state/shutdown IPC.

`Generated/ModuleLoader.cs` is also generated by `Configurator/Program.cs`; changes to generated module loading behavior must be mirrored in the generator.

`Plugins.targets` stages module build outputs into `bin/.../modules`. The worker must shadow-copy from `modules/` into a worker-specific directory before loading assemblies, so subsequent manual builds can overwrite `modules/`.

## Real-Time Assessment

Hot reload orchestration is outside steady-state frame loops. IPC waits, process spawning, file copy, JSON serialization, and ECS snapshot restore are allowed only during reload/startup.

No watcher, file polling, build polling, process waits, or blocking IPC should be added to ECS `Run`, fixed tick, render, or network hot paths. ECS snapshot capture must be scheduled onto the main thread so worlds are not serialized while systems mutate them.

Client/Server boundaries remain selected by `Side`: client launcher stages shared+client modules; server launcher stages shared+server modules; worker loads the matching set only.

## Plan of Work

1. Add `HotReloadMode` and `HotReloadOptions` in `Karpik.Engine.Core`, keeping `CoreRunner.Start(Ref<bool>, Side)` source-compatible and adding an overload with options.
2. Make Debug default use `RestartWorker`; make Release default use direct in-process launch unless options explicitly request restart-worker.
3. Refactor `CoreRunner` so restart-worker and direct launch are separate runtime methods, not compile-time-mixed `SUPER_HOT_RELOAD` behavior.
4. Extend `ProcessManager` to use `HotReloadOptions` timeouts/path/debugger behavior and preserve the old worker if state capture fails.
5. Fix `Karpik.Engine.Core.Runner.Program` to load modules by `Side`.
6. Simplify `Generated/ModuleLoader.cs` and the generator in `Configurator/Program.cs`: shadow-copy from `modules/`, validate required assemblies, load current worker assemblies, and remove unload/static cleanup logic from the reload path.
7. Restrict hot reload state collection to ECS by using a stable state key or explicit supported type check, then update `IInstallerHotReload` documentation/obsolete message to state non-ECS persistence is unsupported in v1.
8. Improve `Plugins.targets` so module deployment uses built project outputs where possible and fails visibly when required module DLLs are absent.
9. Build client and server launchers; record results here.

## Milestones

Milestone 1: Runtime flow compiles.
Expected evidence: `dotnet build Karpik.Engine.Core/Karpik.Engine.Core.csproj` succeeds.

Milestone 2: Client composition compiles and stages modules.
Expected evidence: `dotnet build ClientLauncher/ClientLauncher.csproj` succeeds and `bin/.../modules/ECS.Core.dll` exists.

Milestone 3: Server composition compiles and stages modules.
Expected evidence: `dotnet build ServerLauncher/ServerLauncher.csproj` succeeds and server module DLLs exist without client-only modules required by the server worker.

## Concrete Steps

Commands are run from `C:\Users\artem\RiderProjects\KarpikEngine`.

- `dotnet build Karpik.Engine.Core/Karpik.Engine.Core.csproj`
- `dotnet build ClientLauncher/ClientLauncher.csproj`
- `dotnet build ServerLauncher/ServerLauncher.csproj`

Manual runtime checks after implementation:

- Start `ClientLauncher`, press Hot Reload, verify worker PID changes and game resumes.
- Start `ServerLauncher`, trigger reload, verify server modules are loaded.
- Repeat reload five times and verify no stale worker/pipe/shadow directory blocks reload.

## Validation and Acceptance

Accepted when both launchers build, worker module loading is side-correct, restart-worker is the Debug default, direct mode remains available, and ECS-only state transfer survives the reload flow without relying on ALC unload.

If runtime smoke checks cannot be executed in this environment, record the commands/scenarios and the remaining risk.

## Idempotence and Recovery

Build and reload commands are safe to rerun. Shadow directories are worker-specific and may be deleted when no worker is using them.

If worker startup fails after a reload attempt, watcher should report the error and not claim reload success. If state capture fails, watcher should keep the existing worker alive where possible.

Rollback is straightforward by reverting `CoreRunner`, `ProcessManager`, `Karpik.Engine.Core.Runner.Program`, `Generated/ModuleLoader.cs`, `Configurator/Program.cs`, and `Plugins.targets`.

## Artifacts and Notes

No generated artifacts yet.
