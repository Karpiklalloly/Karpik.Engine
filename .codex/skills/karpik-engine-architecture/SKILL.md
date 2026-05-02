---
name: karpik-engine-architecture
description: Core KarpikEngine architecture rules. Use when Codex changes or reviews Client/Server/Shared boundaries, module lifecycle, Bootstrap, tick/update loops, DI, project structure, or cross-module APIs.
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
- Use `[DI]` fields for systems and services.
- Systems can receive `EcsDefaultWorld`, `Time`, `Application`, and dependencies registered by module installers.
- Services can use `IOnInjectedDI` when work must happen after injection.
- Prefer interfaces for replaceable dependencies and tests.
- Do not use service locator or singleton patterns for gameplay logic.

## Documentation
- Use `docs/02_ADR` for architecture decisions.
- Use `docs/modules/...` for subsystem documentation.
- Use templates from `plans/` for new module plans when useful.
