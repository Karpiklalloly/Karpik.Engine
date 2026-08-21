# Introduce attribute-driven DI with hierarchical scopes

This ExecPlan is a living document. It must be maintained according to `plans/PLANS.md`.

## Purpose / Big Picture

Replace repetitive Autofac registrations in module installers with declarative service exports while retaining installers for registrations that require factories, conditions, external instances, or other custom composition. The runtime will build an ownership hierarchy of `Engine -> ModSet -> Simulation`. Services receive dependencies through constructors, are visible only from their own scope downward, and are disposed by the Autofac scope that owns them.

The first observable outcome is that ordinary services such as asset loaders and savers can be added by annotating their classes. `AssetsManager` receives all visible `IAssetLoader` and `IAssetSaver` implementations through constructor injection, without reflection, `Activator.CreateInstance`, field injection, or `OnAnotherModuleLoaded`.

## Progress

- [x] (2026-08-06) Initial architecture discussed and accepted.
- [ ] Define export, scope, and lifetime metadata contracts.
- [ ] Implement descriptor discovery and validation.
- [ ] Build Engine, ModSet, and Simulation lifetime scopes.
- [ ] Migrate asset management to collection injection and an explicit load context.
- [ ] Migrate existing modules and remove obsolete installer lifecycle interfaces.
- [ ] Add focused lifecycle, conflict, and disposal tests.

## Surprises & Discoveries

- Observation: `AssetsManager` cannot constructor-inject loaders that constructor-inject `IAssetsManager`; that forms a dependency cycle.
  Evidence: `FontLoader` and `ComponentsTemplateLoader` currently load dependent assets through a manager stored by `BaseAssetLoader`.

- Observation: the current `OnAnotherModuleLoaded` callback processes installers rather than distinct assemblies, so a single assembly containing several installers can be scanned repeatedly.
  Evidence: Graphics.Core currently contains separate Engine and Simulation installers.

- Observation: a process-wide asset cache is not correct when different mod sets can resolve the same logical asset path differently.
  Evidence: asset lookup includes mod content paths, so the active mod set changes resource identity and resolution.

## Decision Log

- Decision: Use a hybrid composition model.
  Rationale: Attributes remove repetitive registrations, while installers remain available for factories, conditions, third-party objects, and other complex cases that attributes cannot express cleanly.
  Date/Author: 2026-08-06 / developer and Codex

- Decision: Introduce `ModSet` between `Engine` and `Simulation`.
  Rationale: Mods and their asset formats are known before a ModSet is built, and simulations using that ModSet can share its services and asset cache without leaking state into other mod configurations.
  Date/Author: 2026-08-06 / developer and Codex

- Decision: Keep service contract export separate from scope and lifetime metadata.
  Rationale: An implementation may export several contracts, while it has one owning scope and one instance lifetime.
  Date/Author: 2026-08-06 / developer and Codex

- Decision: Remove `OnAnotherModuleLoaded` from the target architecture.
  Rationale: DI registrations must be known before `ContainerBuilder.Build()`. Post-build module notifications are not a safe registration mechanism and currently introduce ordering and duplicate-scanning behavior.
  Date/Author: 2026-08-06 / developer and Codex

- Decision: Pass an asset load context to loader methods instead of injecting `IAssetsManager` or adding `Init()`.
  Rationale: This breaks the manager-loader constructor cycle and avoids temporal coupling and mutable initialization state.
  Date/Author: 2026-08-06 / developer and Codex

- Decision: Autofac lifetime scopes own disposal of services.
  Rationale: `IDisposable` and `IAsyncDisposable` are service responsibilities; installers must not retain and manually destroy resolved runtime services.
  Date/Author: 2026-08-06 / developer and Codex

- Decision: Lua mod registration is deferred but must not be blocked by the descriptor model.
  Rationale: CLR attributes are only one source of registration descriptors. A future Lua manifest or adapter can produce equivalent descriptors without changing Autofac composition.
  Date/Author: 2026-08-06 / developer and Codex

## Outcomes & Retrospective

No implementation outcome yet. Update this section after each milestone and when the migration is complete.

## Context and Orientation

`Karpik.Engine.Core/IModuleInstaller.cs` currently defines the base installer and additional listener, configuration, destruction, and hot-reload interfaces. Module implementations live throughout `Modules/Client`, `Modules/Server`, and `Modules/Shared`.

`Karpik.Engine.Core/ModuleScope.cs` currently contains `Engine` and `Simulation`. Add `ModSet` so the hierarchy becomes:

    Engine
    `-- ModSet
        `-- Simulation

An Engine service can depend only on Engine services. A ModSet service can depend on Engine and ModSet services. A Simulation service can depend on all three. Parent scopes must never resolve services registered only in a child scope.

`Modules/Shared/AssetManagement/AssetManagement.Core/AssetsManager.cs` currently discovers loaders and savers by scanning module assemblies and creates them through reflection. `BaseAssetLoader.cs` currently retains manager and resolver dependencies. Replace this with Autofac collection injection and a per-call asset load context.

An export is a mapping from an implementation class to an explicitly named service contract. Multiple exports for a collection contract must be intentional. Scope identifies the owning container. Lifetime identifies whether that container owns one instance or creates a new instance for each dependency.

## Real-Time Assessment

The new reflection scan and descriptor validation run only during scope construction, never during `Update`, `FixedUpdate`, ECS `Run`, network pumps, or rendering. Constructor enumeration and any materialization of loader/saver collections are cold-path allocations and are acceptable.

Asset loading is not a frame-hot operation by design, but loader lookup must remain dictionary-based after initialization. Do not scan attributes or enumerate Autofac collections for every asset request. The load context should be an existing reference object or the manager itself, not a per-load allocation.

The change preserves Client, Server, and Shared boundaries: metadata contracts belong in Core, asset abstractions remain Shared, and graphics-specific loaders remain Client. No gameplay tick behavior or fixed-dt semantics change.

Scope creation and destruction are serialized lifecycle operations. Do not resolve or dispose a scope concurrently. Destroy children before parents. Hot reload must not leave static caches containing `Type`, `Assembly`, delegates, or instances originating from collectible module load contexts.

## Plan of Work

Add `ModSet` to `ModuleScope`. Define a repeatable export attribute that requires an explicit contract and records whether multiple implementations are allowed. Define a class-level service registration attribute containing `ModuleScope` and a small lifetime enum with `Singleton` and `Transient`. Singleton means one instance in the exact scope where the descriptor is registered; transient means one instance per dependency resolution.

Create an internal registration descriptor independent of CLR attributes. Attribute scanning converts selected CLR types into descriptors. Validate that implementations are concrete and assignable to their contracts, that each exported type has exactly one service registration declaration, and that a non-collection contract has no competing implementation. Error messages must name the contract, implementation, module, and source assembly. Do not silently use Autofac's last-registration-wins behavior.

Build scopes in order. Scan and validate all registrations applicable to a scope before calling `Build()` or `BeginLifetimeScope(...)`. Apply attribute descriptors first, then allow the relevant installer to add complex registrations. A module discovered after Engine construction must not add Engine registrations. This is especially important for future mods.

Reduce `IModuleInstaller` to module identity, an optional scope-aware custom-registration method, and optional creation of an `IModule` that contributes systems. Remove `IModuleInstallerListener` in the target state. Replace `IModuleInstallerDestroy` with Autofac-owned `IDisposable` and `IAsyncDisposable`. Replace general configure hooks with constructor-injected initializer services invoked after their owning scope is built. Move hot-reload state handling to a dedicated service contract in a later focused migration; retain the legacy interface only while necessary for compilation.

Move `AssetsManager` to ModSet scope. Register every loader and saver as an intentional multi-export and constructor-inject `IEnumerable<IAssetLoader>` and `IEnumerable<IAssetSaver>`. Build the lookup dictionaries once during manager construction or explicit scope initialization.

Introduce `IAssetLoadContext` with the narrow capabilities required during one load operation, including access to dependent asset loading and the file system. Pass it through `IAssetLoader.LoadAsync`, base loader hooks, and default-path resolution. Do not store the context on loaders. Remove manager and service-resolver dependencies from loader constructors where they are used only during loading.

Dispose scopes asynchronously in strict child-to-parent order: Simulation, then ModSet, then Engine. Autofac owns all activated disposable services unless a rare registration is explicitly marked externally owned. Avoid disposable transients in long-lived scopes because Autofac tracks them until scope disposal. ECS `ISystemDestroy` stops system behavior and subscriptions; it must not manually dispose injected services owned by Autofac.

Keep future Lua support out of the first implementation. Preserve an internal descriptor boundary so a future Lua manifest or adapter can provide registrations without requiring CLR attributes.

## Milestones

### Milestone 1: Metadata and validation

Implement scope/lifetime/export metadata and deterministic descriptor discovery for selected assemblies. Add tests for explicit contract mapping, intentional collections, duplicate single-contract failure, invalid implementation contracts, and deterministic results independent of reflection enumeration order.

Validation: run the smallest Core test project containing module composition tests and confirm all new descriptor tests pass.

### Milestone 2: Hierarchical Autofac ownership

Construct Engine, ModSet, and Simulation scopes and prove parent-to-child visibility, child isolation, multiple concurrent ModSets, and multiple simulations under one ModSet. Add sync and async disposable probes and verify child-to-parent teardown.

Validation: run targeted Runner lifetime tests. Expected observations are one singleton per declared owning scope, distinct instances across sibling scopes, and exactly one disposal call for each activated owned service.

### Milestone 3: Installer simplification

Migrate simple registrations to attributes. Keep custom registrations only where factories or conditional composition are required. Remove listener and destroy responsibilities, and introduce constructor-injected scope initializers for explicit startup work.

Validation: build the smallest affected Client and Server compositions and run existing module-order and lifecycle tests.

### Milestone 4: Asset management migration

Move asset management into ModSet scope, inject loader/saver collections, and pass `IAssetLoadContext` into load operations. Remove reflection activation, injection calls, and module-loaded callbacks from asset management.

Validation: run asset manager tests, shader/font loading tests, dependency-loading tests, separate-ModSet cache isolation tests, and disposal tests. Confirm loader dictionaries are built once and no per-frame path is introduced.

### Milestone 5: Cleanup and documentation

Remove obsolete compatibility interfaces after all consumers migrate. Update module architecture documentation and create an ADR for the accepted scope and ownership model.

Validation: run targeted builds for Core, Runner, AssetManagement, Graphics, Client, and Server, followed by the relevant test projects. Refresh the codebase-memory index after code changes.

## Concrete Steps

Run commands from `C:\Users\artem\RiderProjects\KarpikEngine`.

Use targeted single-node builds:

    dotnet build Karpik.Engine.Core/Karpik.Engine.Core.csproj -m:1 -nr:false
    dotnet build Karpik.Engine.Core.Runner/Karpik.Engine.Core.Runner.csproj -m:1 -nr:false
    dotnet build Modules/Shared/AssetManagement/AssetManagement.Core/AssetManagement.Core.csproj -m:1 -nr:false

Run the smallest relevant test projects after each milestone rather than waiting for a full solution build. Discover exact test project paths with:

    rg --files -g "*.csproj" | rg "Tests"

After implementation changes stabilize, update the repository graph:

    # refresh the codebase-memory index

## Validation and Acceptance

Acceptance requires all of the following observable behavior:

- An attributed ordinary service resolves without an installer registration.
- A complex factory registration still works through `IModuleInstaller`.
- Duplicate single-contract exports fail during composition with actionable diagnostics.
- Multiple loader/saver exports resolve as complete collections.
- Engine, ModSet, and Simulation singletons have the expected isolation and visibility.
- A late module cannot add an Engine registration after Engine construction.
- `AssetsManager` has no reflection activation, field injection, resolver dependency, or module-loaded callback.
- Dependent asset loading works through `IAssetLoadContext`.
- Disposing Simulation, ModSet, and Engine scopes invokes synchronous and asynchronous service cleanup exactly once.
- Repeated construction and disposal does not retain collectible module assemblies through static reflection caches.
- No new work or allocations occur in frame, fixed-tick, ECS run, network pump, or render hot paths.

## Idempotence and Recovery

Attribute discovery and descriptor validation are read-only and safe to repeat. Scope creation must either succeed completely or dispose the partially created scope. If initialization fails after a scope is built, dispose that scope asynchronously before propagating the error.

Migrate modules incrementally. Compatibility interfaces may remain temporarily while a consumer still requires them, but the ExecPlan and tests must record which consumers remain. Do not remove old registration paths until the equivalent attributed or custom registration is validated.

If a migration breaks runtime composition, revert only the affected module to its explicit installer registration; do not revert the scope hierarchy or mix old and new ownership for the same service instance. Avoid `git reset --hard` or broad checkout operations in a dirty worktree.

## Artifacts and Notes

- Source of truth for plan format: `plans/PLANS.md`.
- Target durable decision record after implementation: `docs/02_ADR/` entry for hierarchical DI scope and service ownership.
- Modding integration is deliberately deferred. The descriptor abstraction must remain independent of CLR attributes so it can be extended later.
