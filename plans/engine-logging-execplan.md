# Engine Logging Through `ILogger` Implementation Plan

> **For agentic workers:** Execute this plan task by task. Keep its progress current and validate each milestone before starting the next.

**Goal:** Route ordinary engine runtime diagnostics through `ILogger` and let game modules add or replace providers through `ILoggerFactoryModifier`.

**Architecture:** Host entry points create a process-owned `ILoggerFactory` and pass it through Core, WorkerHost, process management, and IPC. The worker engine container creates its own factory after module registrations are known, applies all exported `ILoggerFactoryModifier` implementations to the `ILoggingBuilder`, then supplies `ILogger<T>` through existing DI. The standalone Jobs library accepts an optional error callback so engine use can log through `ILogger` without adding a logging dependency to that library.

**Tech Stack:** .NET 10, Microsoft.Extensions.Logging, Autofac, Karpik.Jobs, DragonECS.

**Spec:** `docs/superpowers/specs/2026-09-26-engine-logging-design.md`

## Global Constraints

- Keep direct console input and CLI/TUI, redirected-process, and editor-protocol output intact.
- Keep logger and provider instances inside the process and scope that own them; do not send them through IPC.
- Apply game modifiers once while creating the worker engine factory, outside gameplay and ECS hot paths.
- Preserve Karpik.Jobs standalone behavior when no callback is supplied.
- Preserve ECS event exception propagation; log at the engine boundary rather than adding global state or breaking game subclass construction.
- Do not add or run tests in this execution unless the user asks; validate with targeted builds and scoped source audits.

## Review Focus

- No game modifier is registered: default console and editor capture providers still work.
- Several modifiers are registered: each is applied once and may add or deliberately replace providers.
- Startup fails before the engine container is built: the host factory still records the exception.
- A job callback throws: the original job exception still completes the handle as failed and worker accounting still completes.
- A logging callback/provider fails during an error path: the original job exception is still completed and worker accounting is released.
- The engine remains overloaded for many ticks: warning output is coalesced and does not allocate or write every tick.

## Progress

- [x] (2026-09-26) Read current logger registration, process-isolation flow, module DI order, and engine console call sites.
- [x] (2026-09-26) Write and review the design spec; approved by the user. Commits: `8ee07a5`, `7706cf1`, `b1b9c39`.
- [x] (2026-09-26) Create this implementation plan.
- [x] Implement provider modifiers.
- [x] Pass host factory through watcher startup, process management, and IPC (Task 2).
- [x] Convert worker/runtime diagnostics (Task 3).
- [x] Convert first-party runtime error paths (Task 4).
- [x] Build affected projects and audit remaining console calls.

## Surprises & Discoveries

- Observation: Watcher and worker are separate processes; game modules load in the worker.
  Evidence: `plans/process-isolation-architecture.md` and `Karpik.Engine.Core.Runner/WorkerHost.cs`.
- Observation: The worker engine container is built after types and installers are known, so game modifiers can be resolved when the engine `ILoggerFactory` is created.
  Evidence: `Karpik.Engine.Core.Runner/Runner.cs`, `SetupCoreAsync` and `RegisterInstallerServices`.
- Observation: `Karpik.Jobs` is also used standalone. A callback avoids making it depend on Microsoft logging.
  Evidence: `Karpik.Engine.Core/Bootstrap.cs` calls `new Jobs.JobSystem()`; the library also has a sample program.
- Observation: Dragon ECS event base systems are user-subclassed and receive their world through Dragon ECS injection only. Their catch blocks log and rethrow the same exception.
  Evidence: `first-parties/DragonECS.Karpik.Extensions/EcsRunners/EventCallers.cs`.

## Decision Log

- Decision: Use a public `ILoggerFactoryModifier` contract with `void Modify(ILoggingBuilder builder)`.
  Rationale: It adds or replaces providers through the installed logging abstraction without exposing Autofac or a concrete factory implementation.
  Date/Author: 2026-09-26 / Codex and user.
- Decision: Keep separate host and worker-engine factory configuration; no factory instance crosses IPC.
  Rationale: The host cannot resolve worker game modules, and provider objects are process-local.
  Date/Author: 2026-09-26 / Codex and user.
- Decision: Use an optional `Action<Exception>` for Karpik.Jobs failures and preserve its current fallback for standalone callers.
  Rationale: The engine can pass an `ILogger`-backed callback without adding a logging package to Karpik.Jobs.
  Date/Author: 2026-09-26 / Codex.
- Decision: Remove duplicate catch/write/rethrow from Dragon ECS event base systems and keep exception propagation to the engine logging boundary.
  Rationale: Passing ILogger into these game-subclassed systems would require changing their creation/injection contract or adding global state.
  Date/Author: 2026-09-26 / Codex and user.

## Outcomes & Retrospective

Implementation complete. All seven affected projects built successfully with single-node MSBuild and node reuse disabled; the final Core and Runner builds used `--no-restore`. No tests were added or run. The scoped console audit found only intentional watcher input/prompt, editor protocol output, standalone Jobs fallback, console color helpers, and the Modding test sample. `git diff --check` passed for the implementation commits and the current working tree; remaining dirty files are the user's pre-existing edits plus this plan and the logger documentation correction.

## Context and Orientation

`Modules/Shared/LoggerModule/LoggerModuleInstaller.cs` currently creates the engine `ILoggerFactory` with `AddSimpleConsole`, optional editor capture, and Trace minimum level. `Karpik.Engine.Core.Runner/Runner.cs` builds the engine container after module types are registered, then applies installer registrations. This is where exported game modifiers can be resolved without changing engine composition order.

The watcher-side `CoreRunner` owns `ProcessManager`; `ProcessManager` creates `IpcServer`, while the worker `WorkerHost` creates `IpcClient`. `Program` and `StaticEngineHost` are worker entry points. These objects currently write lifecycle and IPC diagnostics directly to Console. Pass the host factory through their constructors, keeping logger lifetime with the process entry point.

The engine-specific console sites found in source review are in `Karpik.Engine.Core`, `Karpik.Engine.Core.Runner`, client/server network systems, `Karpik.Jobs.JobSystem`, and the Dragon ECS event base classes. Console use in CLI tools, game templates, tests, vendored DragonECS, the editor protocol logger, and process-output capture is outside this change.

## Real-Time Assessment

- Hot path: Logger additions in `FixedRunTicker`, `CoreRunner.ServerLoop`, and `WorkerHost.ServerLoop` are only on the existing overload branch. Coalesce repeated warnings so the saturated tick does not write each iteration.
- Allocation budget: No allocations are added to normal frame/tick execution. `LoggerFactoryModifier` callbacks and provider creation run once during factory creation. The Karpik.Jobs error callback runs only on job failure.
- Data layout: No ECS component or hot-path data layout changes.
- Side boundary: Client, Server, and Shared modules keep their existing project boundaries.
- Tick behavior: Keep fixed timestep and catch-up limits unchanged.
- Concurrency: Host factories are shared by worker/logging tasks; Microsoft logging providers own their synchronization. Do not hold engine locks while logging.
- Validation: Build each affected project with single-node MSBuild and disabled node reuse, then audit scoped Console writes. Do not add or run tests under the current instruction.

## Plan of Work

### Task 1: Add game factory modifiers

**Files:**

- Create `Modules/Shared/LoggerModule/ILoggerFactoryModifier.cs`.
- Modify `Modules/Shared/LoggerModule/LoggerModuleInstaller.cs`.
- Modify `docs/modules/shared/logger.md`.

**Interface:**

- Produce `public interface ILoggerFactoryModifier { void Modify(ILoggingBuilder builder); }` in `Karpik.Engine.Shared.Log`.
- Exported Engine-scope modifier services are enumerated while the engine `ILoggerFactory` is created. Keep the default console provider and editor-capture provider unless a modifier explicitly changes them.

- [x] Implement the contract and factory registration.
- [x] Build `Modules/Shared/LoggerModule/LoggerModule.csproj -m:1 -nr:false`; expect success.
- [x] Update logger documentation with modifier registration and provider ownership.

### Task 2: Pass host loggers through startup, process management, and IPC

**Files:**

- Modify `Karpik.Engine.Core/CoreRunner.cs` and `Karpik.Engine.Core/Bootstrap.cs`.
- Modify `Karpik.Engine.Core/MainThreadScheduler.cs` and `Karpik.Engine.Core/HotReloadHandler.cs`.
- Modify `Karpik.Engine.Core/ProcessManagement/ProcessManager.cs`, `IpcClient.cs`, `IpcServer.cs`, and `ModuleStagingCleanup.cs`.
- Modify `Karpik.Engine.Core/Editor/EditorPreviewController.cs` only as needed to pass the same host factory.
- Modify `Karpik.Engine.Core/Karpik.Engine.Core.csproj` to use the existing Microsoft console logging provider for default host factories.

**Interfaces:**

- Host entry points own the `ILoggerFactory` they create and pass it to engine-owned children.
- Existing public constructors and start methods remain source-compatible by retaining default-factory overloads.
- `ProcessManager` passes `ILoggerFactory` to the IPC endpoints and staging cleanup; child classes create category loggers once in their constructors.
- Keep console key reads, the watcher hot-reload key prompt, and child stdout/protocol capture untouched.

- [x] Add constructor overloads and logger fields; ensure factories are disposed by their owning process entry point, not by child classes.
- [x] Replace ordinary lifecycle, cleanup, IPC, and exception writes with level-appropriate structured `ILogger` calls, preserving exception objects.
- [x] Remove redundant logging in `HotReloadHandler` where `WorkerHost.RequestHotReload` already records the same event.
- [x] Build `Karpik.Engine.Core/Karpik.Engine.Core.csproj -m:1 -nr:false`; expect success.

### Task 3: Route worker and runner diagnostics through ILogger

**Files:**

- Modify `Karpik.Engine.Core.Runner/Runner.cs`, `WorkerHost.cs`, `FixedRunTicker.cs`, `StaticComposition/StaticEngineHost.cs`, and `Program.cs`.

**Interfaces:**

- `WorkerHost` and `EngineRunner` accept the host logger factory for pre-container and module-registration diagnostics.
- After `_engineContainer` is built, engine services resolve `ILogger<T>` from the container factory, which applies the exported modifiers.
- `FixedRunTicker` receives an `ILogger<FixedRunTicker>` from the simulation service resolver.

- [x] Replace remaining worker and runner diagnostic writes; leave console input, the watcher hot-reload key prompt, and process transport intact.
- [x] Coalesce overload warnings until the loop recovers; keep tick skipping/backlog behavior unchanged.
- [x] Build `Karpik.Engine.Core.Runner/Karpik.Engine.Core.Runner.csproj -m:1 -nr:false`; expect success.

### Task 4: Convert runtime module and first-party error output

**Files:**

- Modify `Modules/Client/Network.Client/Network.Client.LiteNetLib/Systems/InitNetworkClientSystem.cs`.
- Modify `Modules/Server/Network.Server/Network.Server.LiteNetLib/Systems/UpdateSystem.cs`.
- Modify `first-parties/Karpik.Jobs/Karpik.Jobs/JobSystem.cs` and `Karpik.Engine.Core/Bootstrap.cs`.
- Modify `first-parties/DragonECS.Karpik.Extensions/EcsRunners/EventCallers.cs`.

**Interfaces:**

- Inject `ILogger<T>` into the client/server network systems through their existing constructor-based system registration.
- Append optional `Action<Exception>? onJobError = null` to `JobSystem(int workerCount = -1, string prefix = "JobWorker", Action<Exception>? onJobError = null)`; preserve the current console fallback when omitted. Engine `Bootstrap` passes a callback that calls its `ILogger` with the exception. Isolate callback failures so they cannot replace the original job exception or prevent completion/accounting cleanup.
- Event callback base classes keep their existing public subclass and ECS world-injection contracts. Remove catch/log/rethrow blocks; propagated exceptions are recorded by the engine boundary.

- [x] Convert network diagnostics to structured log calls.
- [x] Add the Jobs callback and wire the engine path without adding a logging package to Karpik.Jobs.
- [x] Remove duplicate event error printing while preserving `throw` behavior from callbacks.
- [x] Build both network module projects, `first-parties/Karpik.Jobs/Karpik.Jobs/Karpik.Jobs.csproj`, and `first-parties/DragonECS.Karpik.Extensions/DragonECS.Karpik.Extensions.csproj` with `-m:1 -nr:false`; expect success.

### Task 5: Audit scope and finish documentation

**Files:**

- Review all modified files and `docs/modules/shared/logger.md`.
- No changes to CLI tools, templates, tests, or vendored dependencies.

- [x] Search `Karpik.Engine.Core`, `Karpik.Engine.Core.Runner`, `Modules/Client`, `Modules/Server`, `Modules/Shared`, `first-parties/Karpik.Jobs/Karpik.Jobs`, and `first-parties/DragonECS.Karpik.Extensions` for `Console.Write`, `Console.WriteLine`, `Console.Error`, and `Console.Out`.
- [x] Inspect each remaining occurrence; retain only console input, CLI/TUI/child-process/editor-protocol transport, and standalone Jobs sample behavior.
- [x] Run `git diff --check` and rebuild the smallest affected projects.
- [x] Update this plan's Progress and Outcomes sections with commands and results.

## Milestones

1. The logger module can apply exported modifiers and still builds.
2. Host startup, IPC, worker, and runner diagnostics use ILogger with owned factory lifetimes and still build.
3. Network, Jobs, and event callback paths no longer emit ordinary engine diagnostics directly to Console and their projects build.
4. Scoped Console audit confirms remaining calls are intentional transport/input, and all affected builds succeed.

## Concrete Steps

Run each command from `C:\Users\artem\RiderProjects\KarpikEngine`:

- `dotnet build Modules/Shared/LoggerModule/LoggerModule.csproj -m:1 -nr:false`
- `dotnet build Karpik.Engine.Core/Karpik.Engine.Core.csproj -m:1 -nr:false`
- `dotnet build Karpik.Engine.Core.Runner/Karpik.Engine.Core.Runner.csproj -m:1 -nr:false`
- `dotnet build Modules/Client/Network.Client/Network.Client.LiteNetLib/Network.Client.LiteNetLib.csproj -m:1 -nr:false`
- `dotnet build Modules/Server/Network.Server/Network.Server.LiteNetLib/Network.Server.LiteNetLib.csproj -m:1 -nr:false`
- `dotnet build first-parties/Karpik.Jobs/Karpik.Jobs/Karpik.Jobs.csproj -m:1 -nr:false`
- `dotnet build first-parties/DragonECS.Karpik.Extensions/DragonECS.Karpik.Extensions.csproj -m:1 -nr:false`
- `git diff --check`

Expected: each build succeeds; the scoped source audit finds no accidental engine diagnostic `Console` writes.

## Validation and Acceptance

- Engine and module project builds succeed.
- Game modifier implementations are discovered from Engine-scope registrations and can add or replace providers.
- Default console and editor capture providers are preserved unless deliberately changed by a modifier.
- Host factories are disposed by their owners; IPC and worker lifecycle does not depend on static logger state.
- Job exceptions still complete their handles with the original exception and are sent to `ILogger` in engine use.
- Event callback exceptions continue to propagate and are logged once at the engine boundary.
- Overload warnings are coalesced; fixed timestep and catch-up behavior are unchanged.
- Remaining Console calls are intentional input or transport, or outside the defined engine scope.

## Idempotence and Recovery

The source edits and builds are safe to repeat. If a build identifies a project-reference or public API compatibility issue, preserve existing overloads and correct the dependency direction before continuing. Revert only files changed for this task; do not reset or overwrite the existing unrelated working-tree edits.

## Artifacts and Notes

- Design spec: `docs/superpowers/specs/2026-09-26-engine-logging-design.md`.
- Logger module documentation: `docs/modules/shared/logger.md`.
- No tests will be added or run unless the user asks.





