# Перевести runner на иерархические Autofac scopes

This ExecPlan is a living document. It must be maintained according to `plans/PLANS.md`.

## Purpose / Big Picture

После этой миграции `EngineRunner` больше не использует удалённый `IServiceContainer`, статическую коллекцию сервисов или post-build field injection. Все выбранные модули регистрируют сервисы в одном из трёх вложенных Autofac scopes: `Engine -> ModSet -> Simulation`. Системы сначала объявляются через `IModule.Add(ISystemRegistry)`, затем регистрируются в Simulation scope, разрешаются конструкторной инъекцией и добавляются в ECS pipeline. При остановке pipeline уничтожается до scopes, а Autofac вызывает `IDisposable` и `IAsyncDisposable` у принадлежащих им объектов.

## Progress

- [x] (2026-08-09) Initial plan created after inspecting the partially migrated Core, Runner, modules, and tests.
- [x] (2026-08-09) Replaced legacy service containers and lifecycle injection bridges with Autofac resolution.
- [x] (2026-08-09) Built deterministic Engine, ModSet, and Simulation registrations and resolved systems before pipeline initialization.
- [x] (2026-08-09) Moved restart-worker state capture/restore to `IRestartWorkerStateProvider` services.
- [x] (2026-08-09) Updated lifecycle, scope, system-resolution, disposal, ECS, Input, asset, and template callers.
- [x] (2026-08-09) Ran targeted verification, updated graphify, and recorded the outcome.

## Surprises & Discoveries

- Observation: `Karpik.Engine.Core/Services/AutofacServiceResolver.cs` already provides the intended `IServiceResolver` adapter.
  Evidence: it delegates `Resolve`, `ResolveAll`, and `GetService` directly to an `ILifetimeScope`.
- Observation: the current branch cannot compile because `ServiceProvider` and `EcsServiceProvider` still inherit sealed `ContainerBuilder` and reference removed `IServiceContainer`.
  Evidence: targeted builds fail in `Karpik.Engine.Core/Services/ServiceProvider.cs`; `Runner.cs` also references removed installer lifecycle interfaces and an undeclared `systemRegistry` variable.
- Observation: Engine-scoped graphics services currently depend on `Time` and `ClientFrameMetrics`.
  Evidence: `OpenGLGraphicsBackend` and `MergeThread` constructors request those types, so the existing single-application runner must register both in Engine scope until multi-simulation time ownership is designed separately.
- Observation: RPC code generation still emitted `[DI] EcsEventWorld` fields after runtime field injection was removed.
  Evidence: repository-wide legacy-DI search found the templates in both `RpcGenerator` and `TargetClientRpcGenerator`; they now emit constructor dependencies and the generator builds successfully.
- Observation: the complete solution reaches every engine/module project but Avalonia telemetry cannot write its log under the sandboxed `%LocalAppData%`.
  Evidence: the only solution-build errors are `AvaloniaStatsTask` `UnauthorizedAccessException` failures in `Karpik.Editor` and `Karpik.Launcher`; client/server publish assemblies and all module projects compile before that failure.

## Decision Log

- Decision: Build all three scopes now, even though the current runner owns one simulation and has no ModSet registrations yet.
  Rationale: this establishes the agreed parent-child visibility and avoids another container rewrite when mod sets arrive.
  Date/Author: 2026-08-09 / Codex
- Decision: Attribute registrations are applied before explicit installer registrations in each scope.
  Rationale: Autofac uses the last default registration, so explicit complex installer configuration can intentionally override conventional exports.
  Date/Author: 2026-08-09 / Codex
- Decision: Keep `Destroy()` as the synchronous public boundary but implement it through asynchronous scope disposal.
  Rationale: existing hosts are synchronous, while the agreed ownership contract requires `IAsyncDisposable` services to be awaited.
  Date/Author: 2026-08-09 / Codex
- Decision: Register `Time`, `Application`, `MainThreadScheduler`, and `ClientFrameMetrics` in Engine scope for the current one-application runner.
  Rationale: existing Engine services require them. Splitting per-simulation time is a future multi-simulation runner change, not safe to infer during this migration.
  Date/Author: 2026-08-09 / Codex

## Outcomes & Retrospective

`EngineRunner` now owns a deterministic Autofac hierarchy `Engine -> ModSet -> Simulation`. Conventional exports are registered by exact scope and lifetime, explicit installer registrations remain the override mechanism, systems are resolved through constructor injection before pipeline initialization, and restart-worker state is handled by scoped providers. Pipeline destruction precedes reverse-order asynchronous scope disposal, so Autofac owns both synchronous and asynchronous service cleanup.

The migration also removed the obsolete container implementations and Dragon lifecycle field injection, updated affected ECS/Input/asset tests and game templates, and changed RPC code generation to constructor-inject `EcsEventWorld`.

Targeted Runner tests pass 17/17, ECS tests pass 34/34, and Input tests pass 7/7. Core, Runner, Network.Codegen, and both publish projects build. Configurator module-graph validation passes. The full solution compiles all engine/module projects but is not green inside the sandbox because Avalonia telemetry cannot write `%LocalAppData%`; the unfiltered Runner suite is likewise blocked by sandbox denial of named-pipe access in unrelated IPC/process tests. Graphify was updated successfully after granting the required local process/file access.

## Context and Orientation

`Karpik.Engine.Core/IModuleInstaller.cs` now exposes only `Name`, `OnRegisterServices(ContainerBuilder)`, and optional `CreateModule()`. `ModuleAttribute.Scope` selects Engine, ModSet, or Simulation. Exported service classes carry both `System.Composition.ExportAttribute` and `ServiceRegistrationAttribute`, whose lifetime is Singleton or Transient.

`Karpik.Engine.Core.Runner/Runner.cs` is still the old composition root. It creates `ServiceProvider`/`EcsServiceProvider`, calls removed `IModuleInstallerConfiguratable`, listener, destroy, and hot-reload contracts, injects systems after pipeline construction, and manually maintains service instances. `Karpik.Engine.Core.Runner/SystemRegistry.cs` already implements the new two-phase system flow: collect descriptors, register their types in a `ContainerBuilder`, then resolve and add them to an `IBuilder`.

`IRestartWorkerStateProvider` is the new restart-worker state boundary. Simulation services such as `EcsRestartWorkerStateProvider` expose a stable `Key`, `Capture()`, and `Restore(ReadOnlySpan<byte>)`.

## Real-Time Assessment

Composition, reflection scanning, and scope construction occur only during setup/restart, never in `Update`, `FixedUpdate`, render, ECS iteration, or network pumps. Attribute scanning and Autofac resolution may allocate during startup; no new per-frame allocation is introduced. Existing pipeline loops and data layout are unchanged. The change remains in Shared/Core boundaries and introduces no Client-to-Server or Server-to-Client dependency. No new locks, waits, or background work are added. Scope disposal happens during shutdown, outside the frame loop. Validation focuses on deterministic registration order, constructor resolution, parent visibility, restart state ordering, and sync/async disposal.

## Plan of Work

Remove the obsolete service-container implementations and simplify `DragonExtensions/LifeCycleBridge.cs` to wrappers that only call already-resolved systems. Add a focused attributed-service registrar in Runner that validates exports, registers every advertised contract, and applies the requested Autofac lifetime for one exact `ModuleScope`.

Refactor `EngineRunner.Setup` into a deterministic composition sequence. Sort modules, collect `IModule` system descriptors, build Engine registrations, create a ModSet child scope, then create a Simulation child scope containing Simulation exports, installer registrations, system types, and the local `IServiceResolver`. Resolve restart-state providers and restore state before resolving systems and initializing the pipeline. Keep the built scopes in fields for runtime resolution and ordered disposal.

Replace old installer callbacks, listener notifications, field injection, and installer destruction with the reduced contract and Autofac ownership. Capture restart state from all resolved `IRestartWorkerStateProvider` instances and reject duplicate keys. Destroy the pipeline and schedulers before asynchronously disposing Simulation, ModSet, then Engine.

Update Runner tests to use scoped module attributes, `CreateModule`, and `ISystemRegistry`. Add tests proving constructor injection, Engine-to-Simulation visibility, Simulation isolation from Engine resolution, singleton/transient behavior, restart-state restore before system init, and reverse scope disposal including async-only services. Update directly affected legacy test fixtures to use `AutofacServiceResolver` instead of deleted containers.

## Milestones

1. Core compiles without `IServiceContainer`, `ServiceProvider`, or lifecycle bridge injection. Validate with `dotnet build Karpik.Engine.Core/Karpik.Engine.Core.csproj --no-restore -m:1 -nr:false` and `dotnet build DragonExtensions/DragonExtensions.csproj --no-restore -m:1 -nr:false`.
2. Runner composes all scopes, exports, installers, systems, and restart state. Validate with `dotnet build Karpik.Engine.Core.Runner/Karpik.Engine.Core.Runner.csproj --no-restore -m:1 -nr:false`.
3. Runner lifecycle and DI tests pass. Validate with `dotnet test Karpik.Engine.Core.Runner.Tests/Karpik.Engine.Core.Runner.Tests.csproj --no-restore -m:1 -nr:false`.
4. Directly affected ECS and Input tests compile or pass, followed by `graphify update .`. Record all results and remaining unrelated failures here.

## Concrete Steps

All commands run from `C:\Users\artem\RiderProjects\KarpikEngine`.

1. Inspect diffs before every edit with `git diff -- <paths>` to preserve the developer's in-progress module migration.
2. Edit files only through `apply_patch`.
3. Run each milestone command exactly as listed above and record its result in `Progress` before moving on.
4. Run `graphify update .` after code changes succeed.

## Validation and Acceptance

Acceptance requires a clean targeted Core and Runner build plus passing Runner tests. Tests must observe that Engine services are visible in Simulation, Simulation services do not become resolvable from Engine, Singleton returns one object per owning scope, Transient returns distinct objects, systems with constructor dependencies execute, restart data restores before `ISystemInit`, and both `Dispose()` and `DisposeAsync()` are called exactly once after pipeline destruction. Existing lifecycle order `Init -> Begin -> Fixed/Update/Late/Render -> Destroy` must remain intact.

No allocation benchmark is required because all edited reflection and DI code is startup/shutdown-only. Existing frame-loop tests must still pass to prove no lifecycle regression.

## Idempotence and Recovery

Builds and tests are safe to rerun. Setup must reject a second active setup rather than leaking old scopes. Destroy must be idempotent so setup failures and host `finally` blocks can call it safely. If scope creation fails, dispose every successfully created child in reverse order before rethrowing. Do not use git reset or overwrite unrelated working-tree changes; revert only the files named by this plan with a reviewed patch if recovery is necessary.

## Artifacts and Notes

- Living plan: `plans/autofac-runner-scopes-execplan.md`
- Architecture source: `plans/PLANS.md`
- Primary implementation: `Karpik.Engine.Core.Runner/Runner.cs`
- Attribute registration helper: `Karpik.Engine.Core.Runner/AttributedServiceRegistrar.cs`
- Primary tests: `Karpik.Engine.Core.Runner.Tests/Program.cs`
