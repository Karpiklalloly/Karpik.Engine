---
name: karpik-engine-architecture
description: Use when Codex changes or reviews KarpikEngine Client/Server/Shared boundaries, modules, Bootstrap, DI registration or injection, service scopes, tick loops, project structure, or cross-module APIs.
---

# KarpikEngine Architecture

## Client / Server / Shared
- `Client` contains rendering, input, and client presentation logic.
- `Server` contains server logic, authority, and validation.
- `Shared` contains common code independent of runtime side.
- Use `Side` enum for side selection and `#if SERVER` / `#if CLIENT` only when separate compilation is required.

Forbidden:

- importing Server projects into Client or Client projects into Server;
- duplicating logic that should live in Shared;
- leaking client graphics or input types into Shared or Server.

## Module System
A module must be an independent lifecycle unit:

- use `IModule` for module contracts;
- use `ModuleAttribute` for registration;
- initialize through `Bootstrap` and established lifecycle hooks;
- connect modules through interfaces rather than concrete implementations.

Do not do heavy work in module constructors. Initialization must be explicit and measurable.

## Application & Tick System
- Base tick rate: `TICKS_PER_SECOND`, default 50.
- `TICK_DT = 1.0 / TICKS_PER_SECOND`.
- Loop order: `Update -> FixedUpdate -> Render`.
- Physics and game logic use fixed dt.
- `Update` and ECS `Run` are hot paths: no allocations and no blocking work.

## Dependency Injection
- Register ordinary services declaratively with both `[Export]` and `[ServiceRegistration(scope, lifetime)]`.
- Use `IModuleInstaller.OnRegisterServices(ContainerBuilder)` for factories, existing instances, conditional registrations, third-party types, or an intentional override of an attribute registration.
- Inject services and systems only through constructors. Do not add new `[DI]` field/property injection or `IOnInjectedDI` usage.
- Declare ECS system types through `IModule.Add(ISystemRegistry)`; the runner resolves their constructor dependencies from the Simulation scope.
- Prefer interfaces for replaceable dependencies and tests.
- Do not manually construct registered services or use a service locator for statically known dependencies.
- Autofac owns and disposes registered `IDisposable` and `IAsyncDisposable` services with their scope.

Attribute registration is the default:

```csharp
[Export(typeof(IFoo))]
[ServiceRegistration(ModuleScope.Simulation, ServiceLifetime.Singleton)]
public sealed class Foo(IClock clock) : IFoo { }
```

`[Export]` means `System.Composition.ExportAttribute`. Explicit installer registrations are applied after attribute registrations in the same scope and therefore may override them intentionally.

## Service Scopes
Scopes form the parent-child chain `Engine -> ModSet -> Simulation`. A child can resolve parent exports; a parent cannot resolve child services, and sibling scopes are isolated.

- `Engine`: worker-wide infrastructure shared by every mod set and simulation. It is disposed last.
- `ModSet`: services and state shared by simulations created for one fixed set of loaded mods. It sees Engine services and is disposed after its simulations.
- `Simulation`: ECS worlds and mutable state of one running simulation. It sees ModSet and Engine services and is disposed first when that simulation stops or restarts.

`Singleton` means one instance per owning scope, not one process-global instance. `Transient` creates a new instance per resolution. Do not move mutable gameplay or simulation state into a parent scope merely to share it.

## Documentation
- Use `docs/02_ADR` for architecture decisions.
- Use `docs/modules/...` for subsystem documentation.
- Use `docs/01_Architecture/dependency-injection-and-scopes.md` as the canonical DI and scope reference.
- Use templates from `plans/` for new module plans when useful.
