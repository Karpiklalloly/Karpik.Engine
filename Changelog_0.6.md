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
