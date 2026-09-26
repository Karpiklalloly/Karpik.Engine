# Engine Logging Through `ILogger`

## Purpose

Replace ordinary diagnostic console output in KarpikEngine runtime code with `Microsoft.Extensions.Logging.ILogger`. Keep logs configurable by the game so a game can add or replace logging providers without editing the engine's logger module.

## Scope

The refactor covers engine runtime and engine modules: `Karpik.Engine.Core`, `Karpik.Engine.Core.Runner`, client/server/shared runtime modules containing diagnostic `Console` calls, and the engine-owned runtime integrations in `first-parties/Karpik.Jobs/Karpik.Jobs` and `first-parties/DragonECS.Karpik.Extensions`.

Keep direct console access where it is part of a user-facing CLI/TUI, console input, redirected child-process transport, editor log protocol, standalone library sample, or third-party/test code. Do not migrate standalone tools, templates, or vendored dependencies as part of this change.

The standalone Karpik.Jobs API remains usable without an engine logger. Add an optional job-error callback; when the engine initializes it, failures are forwarded to `ILogger`, while standalone callers keep the current console fallback.

## Design

Create a host-owned `ILoggerFactory` at each process entry point and pass it through engine-owned startup, process-management, worker, and IPC constructors. Existing public entry points remain source-compatible by retaining overloads that create the default factory. The engine DI container registers its `ILoggerFactory` as an `ILoggerFactory` and resolves `ILogger<T>` through the existing Microsoft logging implementation.

Add a public `ILoggerFactoryModifier` interface in the shared logger module. Game modules can export implementations; the logger module resolves all implementations while creating the engine `ILoggerFactory` and calls each modifier with the `ILoggingBuilder`. This lets a game add logging providers or deliberately clear/replace defaults without depending on Autofac or editing engine code. The default console provider and editor-capture provider remain enabled by default. Modifiers run once per factory creation, outside gameplay and ECS hot paths. Provider disposal remains owned by the factory.

Because watcher and worker are separate processes and game module assemblies are loaded in the worker, no logger or provider instance crosses IPC. Watcher and worker startup/process-management logs use their host factories. Game-provided modifiers configure the engine factory in the worker, where game services and engine runtime logs run. This keeps factory configuration inside the process and DI scope that owns the game modules.

Use `LogInformation`, `LogWarning`, and `LogError` according to message meaning, retaining exception objects in error calls. Do not emit overload diagnostics every tick indefinitely; rate-limit or coalesce repeated overload messages. Preserve `Console` reads for controls and console writes used as protocol/CLI/TUI transport.

Dragon ECS event callback systems are user-subclassed and currently receive only the ECS world through injection. Do not add a global logger or break that public injection API for these wrappers. Remove their duplicate catch/write/rethrow blocks and keep exception propagation; the engine boundary records the exception through `ILogger`.

## Observable behavior

- Engine runtime diagnostics are emitted through `ILogger` and the configured providers.
- A game can add an `ILoggerProvider` through an exported `LoggerFactoryModifier` without replacing engine code.
- Existing default console and editor capture behavior continues unless a game modifier changes providers.
- Console-based input and explicit output protocols continue to work.

## Validation

- Build the smallest affected core, runner, and network-module projects.
- Search the scoped runtime/module source for remaining direct console writes and review each as input, protocol, CLI/TUI, or an accidental diagnostic.
- Confirm overload messages are not emitted on every saturated tick.
