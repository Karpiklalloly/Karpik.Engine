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
- [x] (2026-05-16 13:52:33 +04:00) Added a stable reconnect handshake so client worker restart reattaches to an existing server-side player entity instead of spawning a duplicate.
- [x] (2026-05-16 14:26:01 +04:00) Replaced client-generated session identity with a server-issued reconnect token persisted in ECS.
- [x] (2026-05-16 14:26:01 +04:00) `dotnet build ServerLauncher/ServerLauncher.csproj -m:1 /nr:false` succeeded with 7 warnings and 0 errors.
- [x] (2026-05-16 14:26:01 +04:00) `dotnet build ClientLauncher/ClientLauncher.csproj -m:1 /nr:false` succeeded with 61 warnings and 0 errors.
- [x] (2026-05-16 22:28:01 +04:00) Fixed player accumulation by treating `PlayerConnection` as runtime peer binding and allowing valid reconnect tokens to hand off an existing player even before the old peer disconnect event arrives.
- [x] (2026-05-16 22:28:01 +04:00) `dotnet build ServerLauncher/ServerLauncher.csproj -m:1 /nr:false` succeeded with 7 warnings and 0 errors.
- [x] (2026-05-17 17:56:28 +04:00) Fixed client-side entity accumulation by rebuilding the generated network-id cache from restored ECS entities before applying the first snapshot after hot reload.
- [x] (2026-05-17 17:56:28 +04:00) `dotnet build ClientLauncher/ClientLauncher.csproj -m:1 /nr:false` succeeded with 62 warnings and 0 errors.
- [x] (2026-05-17 17:56:28 +04:00) `dotnet build ServerLauncher/ServerLauncher.csproj -m:1 /nr:false` succeeded with 7 warnings and 0 errors.
- [x] (2026-05-17 18:43:01 +04:00) Fixed server level duplication after hot reload by making `LevelInitSystem` idempotent and advancing `NetworkIdGenerator` before creating fresh level entities.
- [x] (2026-05-17 18:43:01 +04:00) Removed unsafe generated duplicate-entity cleanup; generated client snapshot cache now only binds to restored `NetworkId` entities and does not delete arbitrary restored entities.
- [x] (2026-05-17 18:43:01 +04:00) Made client `SpriteRenderer` and `IgnoreSpriteData` runtime-only across worker restart by clearing them during `ApplySpriteSystem.Init()`.
- [x] (2026-05-17 18:43:01 +04:00) `dotnet build ServerLauncher/ServerLauncher.csproj -m:1 /nr:false` succeeded with 8 warnings and 0 errors.
- [x] (2026-05-17 18:43:01 +04:00) `dotnet build ClientLauncher/ClientLauncher.csproj -m:1 /nr:false` succeeded with 61 warnings and 0 errors.
- [x] (2026-05-17 19:04:16 +04:00) Moved the client reconnect token into a dedicated `ClientReconnectSession` ECS component so it does not depend on snapshot/RPC ordering for the player entity.
- [x] (2026-05-17 19:04:16 +04:00) Made `SetLocalPlayerTargetRpc` create a lightweight local entity with the target `NetworkId` when the snapshot for that player has not arrived yet.
- [x] (2026-05-17 19:04:16 +04:00) `dotnet build ClientLauncher/ClientLauncher.csproj -m:1 /nr:false` succeeded with 61 warnings and 0 errors.
- [x] (2026-05-17 19:04:16 +04:00) `dotnet build ServerLauncher/ServerLauncher.csproj -m:1 /nr:false` succeeded with 7 warnings and 0 errors.
- [x] (2026-05-17 19:14:57 +04:00) Added server-side disconnected player TTL cleanup and queued deleted `NetworkId` values into snapshot deletion output.
- [x] (2026-05-17 19:14:57 +04:00) Made the client send a handshake in `OnConfigureComplete` if the peer is already connected before the `PeerConnectedEvent` handler is attached.
- [x] (2026-05-17 19:14:57 +04:00) `dotnet build ClientLauncher/ClientLauncher.csproj -m:1 /nr:false` succeeded with 61 warnings and 0 errors.
- [x] (2026-05-17 19:14:57 +04:00) `dotnet build ServerLauncher/ServerLauncher.csproj -m:1 /nr:false` succeeded with 7 warnings and 0 errors.
- [x] (2026-05-17 19:28:39 +04:00) Added a client reconnect-token store keyed by the launcher hot-reload pipe name, so worker restarts can send the server-issued token even if ECS/RPC ordering fails.
- [x] (2026-05-17 19:28:39 +04:00) `dotnet build ServerLauncher/ServerLauncher.csproj -m:1 /nr:false` succeeded with 6 warnings and 0 errors.
- [x] (2026-05-17 19:28:39 +04:00) `dotnet build ClientLauncher/ClientLauncher.csproj -m:1 /nr:false` succeeded with 62 warnings and 0 errors after rerunning separately; the first parallel attempt hit a locked `Karpik.Engine.Core.Runner.dll` in `obj`.
- [x] (2026-05-17 19:44:41 +04:00) Added watcher console hotkey `R` for manual restart-worker hot reload, so `ServerLauncher` can reload the server worker without an in-game client UI.
- [x] (2026-05-17 19:44:41 +04:00) `dotnet build ServerLauncher/ServerLauncher.csproj -m:1 /nr:false` succeeded with 7 warnings and 0 errors; an earlier parallel build hit a locked `StaticAnalyzer.dll`.
- [x] (2026-05-17 19:53:13 +04:00) Fixed server hot reload crash on Windows pipe reuse by allowing multiple named-pipe server instances, disposing stale IPC server state before each worker start, and catching manual reload exceptions in the watcher loop.
- [x] (2026-05-17 19:53:13 +04:00) `dotnet build ServerLauncher/ServerLauncher.csproj -m:1 /nr:false` succeeded with 1 warning and 0 errors.
- [x] (2026-05-17 19:57:39 +04:00) Fixed server hot reload crash from reading `NetworkId` pools inside `OnDelEntity`; deleted network ids are now read from an `entity -> networkId` cache populated while the ECS world is stable.
- [x] (2026-05-17 19:57:39 +04:00) `dotnet build ServerLauncher/ServerLauncher.csproj -m:1 /nr:false` succeeded with 7 warnings and 0 errors.
- [x] (2026-05-17 20:01:46 +04:00) Fixed server worker ticking once after ECS state capture destroyed worlds; `ServerLoop` now exits immediately after `_stateCollected`, matching the existing client loop guard.
- [x] (2026-05-17 20:01:46 +04:00) `dotnet build ServerLauncher/ServerLauncher.csproj -m:1 /nr:false` succeeded with 6 warnings and 0 errors.
- [x] (2026-05-17 20:17:00 +04:00) Made physics body handles runtime-only across worker restart by adding persistent `PhysicsBodyDefinition` data and re-queuing `CreateBodyRequest` during physics module init.
- [x] (2026-05-17 20:17:00 +04:00) `dotnet build ServerLauncher/ServerLauncher.csproj -m:1 /nr:false` succeeded with 9 warnings and 0 errors.
- [x] (2026-05-17 20:17:00 +04:00) `dotnet build ClientLauncher/ClientLauncher.csproj -m:1 /nr:false` succeeded with 62 warnings and 0 errors.

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
- Observation: Network connection identity and gameplay player identity must be separated for restart-worker client reload.
  Evidence: Server-side `NetworkSystem` previously spawned a player directly in `PeerConnectedEvent`; a restarted client gets a new `IPeer`, so this created duplicate player entities. The fix adds `PacketType.Handshake`, ECS `PlayerSession`/`PlayerConnection` components, and server-side attach-or-create logic keyed by a server-issued reconnect token.
- Observation: `PlayerConnection.Connected` is not a reliable persisted boundary.
  Evidence: During client hot reload the new handshake can arrive before the old disconnect event, and during server worker restore the old `Connected=true` value may survive without a real peer mapping. Reconnect must key on `PlayerSession.ReconnectToken`; `PlayerConnection` is reset on server network init and only describes the current worker's peer binding.
- Observation: Client snapshot cache must be restored from ECS after worker restart.
  Evidence: Client hot reload restores ECS entities with `NetworkId`, but a new `NetworkManager` starts with empty `_netToEnt`/`_entToNet` dictionaries. Generated `ApplySnapshot` previously created new entities for known server network ids instead of binding snapshots to restored entities, so visible players could still accumulate on the client.
- Observation: Level bootstrap must be idempotent after ECS restore.
  Evidence: Server `MyGameServerModule` runs `LevelInitSystem` before `NetworkSystem`. With `NetworkIdGenerator.EnsureAtLeast(...)` only in `NetworkSystem.Init()`, a server hot reload restored existing level entities and then created a second level using reused network ids. `LevelInitSystem` now advances the id generator itself and skips level creation when restored `Platform` entities already exist.
- Observation: Generic duplicate cleanup in generated snapshot code is unsafe.
  Evidence: Deleting arbitrary entities with duplicate `NetworkId` can remove the visible/restored platform instead of the newer duplicate. The generated client cache rebuild now binds to the first restored entity for each `NetworkId` and leaves deletion policy to game-specific systems.
- Observation: Client render resources are runtime state, not hot reload state.
  Evidence: `SpriteRenderer` stores loaded texture handles and `IgnoreSpriteData` suppresses renderer creation. After worker restart, restored runtime render components could stop `ApplySpriteSystem` from recreating texture-backed renderers from networked `SpriteData`, causing platforms to disappear visually.
- Observation: The reconnect token must not live only on the replicated player entity.
  Evidence: `SetLocalPlayerTargetRpc` can arrive before the unreliable snapshot that creates the entity with `LocalPlayerNetId`. Storing the token only on the player entity can fail or attach it to the wrong local entity, causing the next client hot reload handshake to send `0` and the server to create a new player.
- Observation: TTL only controls how long a disconnected player can wait; it cannot preserve position if reconnect identity is lost.
  Evidence: With a longer TTL, duplicate player entities remain visible longer. With a shorter TTL, the old positioned entity expires before the client can reattach, so the server creates a new player at spawn. The fix must make the client reliably send the same server-issued reconnect token after worker restart.
- Observation: `PhysicsBodyRef` is a process-local runtime handle, not stable hot reload state.
  Evidence: After server hot reload, `PhysicsPullSystem` restored stale `PhysicsBodyHandle` values and `AetherPhysicsWorld.GetTransforms()` dereferenced a null body slot in the fresh Aether world. `CreateBodyRequest` had already been deleted after initial body creation, so the snapshot no longer contained enough data to recreate bodies.

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

Implemented the restart-worker hot reload code path and verified `Karpik.Engine.Core`, `ClientLauncher`, and `ServerLauncher` build. Client module staging includes `ECS.Core`, `Graphics.Core`, and `Network.Client.Core`; server staging includes `ECS.Core` and `Network.Server.Core` and does not include `Network.Client.Core`. Client reconnect after worker restart now sends a reconnect-token handshake so the server reattaches the new peer to the existing player entity when ECS state is present.

The reconnect token is now server-issued. On first connect the client sends token `0`, the server creates a player with a cryptographically random positive `long` token, and returns that token in `SetLocalPlayerTargetRpc`. The client persists it in ECS via `PlayerSession`, so after hot reload it sends the stored token in the handshake. The server reattaches a matching token to the existing player and replaces any stale peer binding; missing, stale, or zero identities create a fresh player/token instead.

Generated client snapshot application now seeds its network-id cache from restored ECS entities before reading destroyed ids and snapshot entities. This keeps restored client entities, including the local player with `PlayerSession`, bound to incoming snapshots instead of duplicating them after reload. Server level initialization is idempotent after ECS restore, and client sprite runtime state is recreated from networked `SpriteData` instead of trusting restored texture handles.

Client reconnect identity is stored in `ClientReconnectSession`, a client-side ECS component independent of the replicated player entity. `SetLocalPlayerTargetRpc` writes the token there immediately and creates a minimal local `NetworkId` entity if the snapshot has not arrived yet, so the token survives hot reload even under unreliable snapshot ordering.

Disconnected server player entities are now treated as pending reconnects instead of permanent state. `NetworkSystem` assigns a short reconnect TTL, removes the cleanup marker when a matching token reconnects, and deletes expired disconnected players. Deleted network entity ids are queued into snapshot deletion output so clients remove stale local copies instead of keeping visual duplicates.

The client also persists the server-issued reconnect token in a launcher-session file under `reload/client-session`, keyed by the hot-reload pipe name. This is connection identity for the developer hot-reload worker, not gameplay state; gameplay state still survives through ECS. The file store is read only during handshake and written only when `SetLocalPlayerTargetRpc` arrives.

Manual restart-worker hot reload is now exposed at the watcher level: pressing `R` in the launcher console calls `ProcessManager.HotReloadAsync()`. This works for `ServerLauncher` as well as `ClientLauncher`; the client in-game button remains just a worker-side request path.

Windows can keep the previous named pipe instance busy briefly after worker restart. `IpcServer` now allows multiple server instances for the same pipe name and `ProcessManager` disposes stale IPC state before starting a replacement worker, preventing `All pipe instances are busy` from crashing server hot reload.

Entity deletion callbacks can run while Dragon ECS is flushing pending deletes from another query. `NetworkSystem.OnDelEntity` must not call `GetPool<NetworkId>()` during that callback. Deleted network ids are now pulled from a cache that is rebuilt at server network init and refreshed before snapshots.

Server state capture runs through the main-thread scheduler and `ECSInstaller.OnPrepareHotReload()` destroys ECS worlds after serializing them. The server loop must not run another tick after that scheduler action. `ServerLoop` now checks `_stateCollected` immediately after `mainThreadScheduler.Execute()` and exits before `_bootstrap.Loop(...)`.

Physics runtime handles are also outside the reload boundary. `PhysicsBodyRef` stores an index into the current worker's `IPhysicsWorld2D`, so restored refs are cleared during physics init. Body shape/config data is persisted in `PhysicsBodyDefinition`, and missing runtime bodies are recreated by queuing `CreateBodyRequest` before the first physics begin step.

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
