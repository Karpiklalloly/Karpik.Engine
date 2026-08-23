---
title: "Static runtime composition and NativeAOT hosts"
date: "2026-08-23"
status: "accepted"
tags:
  - adr
  - architecture
  - build
---

# Static Runtime Composition and NativeAOT Hosts

> Status: accepted
> Date: 2026-08-23
> Owners: engine core / SDK
> Related ExecPlan: [[../../plans/static-composition-nativeaot-execplan]]

## Context

Dynamic composition (the universal `Karpik.Engine.Core.Runner`) discovers module installers,
services, and systems through runtime reflection: `Assembly.GetTypes`, `Activator.CreateInstance`,
and attribute scanning (`AttributedServiceRegistrar`). This is incompatible with NativeAOT and
defeats trimming, and it makes the set of loaded assemblies observable only at runtime.

Real-time constraints make closed-world composition preferable anyway: no loader passes in the hot
path, deterministic startup, predictable memory layout, and trimmable/AOT-compilable binaries.
Client/Server side boundaries must stay enforceable at compile time.

## Decision

1. **Closed-world composition is the default.** `KarpikCompositionMode` normalizes to `Static`
   when unset (`Sdk.props`); explicit `Dynamic` remains a supported rollback opt-in for one
   release cycle. A source generator (`Karpik.Engine.Core.Codegen`) emits
   `GeneratedRuntimeComposition` with direct module installer instantiations and typed service
   factories; ECS systems are registered through generated factories instead of Autofac type
   reflection.

2. **Game-specific static hosts.** Each launcher (`*.Server.Launcher`, `*.Client.Launcher`) is the
   self-contained game executable holding the generated composition. Static outputs contain no
   managed module manifest (`modules.list`), no `modules.version.*` staging, and no
   `PluginLoadContext`; `RuntimeBundleLayout.ValidateStatic` enforces the shape and static hosts
   report `AppContext.BaseDirectory` as their module directory.

3. **Visibility contract.** Every ECS system and every service consumed across assembly boundaries
   must be visible from the host launcher assembly. Internal systems/services stay Dynamic-only;
   `EcsSystemVisibilitySourceTests` keeps new internal systems out of `Modules/**`.

4. **NativeAOT support surface.**
   - Editor IPC snapshots serialize through a source-generated `EditorSnapshotJsonContext`
     (reflection-based `System.Text.Json` is disabled under AOT).
   - The generator emits static `ComponentTemplate<T>`/`TagComponentTemplate<T>` instantiation
     roots for every discovered component, because runtime `MakeGenericType` has no native code.
   - `ComponentArrayConverter` deserializes through non-generic
     `JObject.ToObject(Type, JsonSerializer)`; runtime generic-method instantiation is banned by
     `EcsStateSerializationSourceBoundaryTests`.
   - Static+AOT publishes root all first-party module assemblies plus DragonECS and Newtonsoft.Json
     (`_KarpikRootStaticGraphReflectionMetadata`) because the hot-reload state pipeline reflects
     over their metadata; third-party payloads stay trimmed.

5. **Warning inventory.** Documented aggregate suppressions for Static+`PublishAot`
   (`Sdk.targets`): IL2104/IL3053 from unannotated payload assemblies; IL3000/IL3002 from Silk.NET's
   native-path probing (`Assembly.Location` is empty under single-file AOT). Both AOT acceptance
   gates fail on any other IL2xxx/IL3xxx/IL3050 code.

6. **Rollback mode.** Switching a project back to `KarpikCompositionMode=Dynamic` restores the
   canonical dynamic bundle layout and runner workflow; bundle completion/recovery machinery
   accepts either complete layout so mode switches replace outputs atomically.

## Alternatives Considered

- **Keep Dynamic as default** — rejected: leaves AOT/trimming unreachable and keeps reflection in
  startup paths.
- **Interpreter-based AOT generics / full-metadata rooting of everything** — rejected: rooting all
  referenced assemblies exceeded 30-minute publish times; narrow rooting plus generated generic
  instantiation roots keeps publishes around 5 minutes.
- **Source-generated state serialization for the whole hot-reload pipeline** — the correct long
  term replacement, but too large for this plan; the rooted-metadata approach is the documented
  interim (see Consequences).
- **Deleting the Dynamic path now** — deferred to a separate ExecPlan after one release cycle of
  telemetry.

## Consequences

- Static hosts start without loader reflection; server and client hosts are fully AOT-publishable
  and pass ten process-isolated reload cycles with preserved ECS state.
- Saved-state payload grows linearly by design when gameplay accumulates entities per start; a
  compounding leak fails the gate (median-delta bound in the acceptance test).
- Reflection-dependent subsystems inside first-party modules keep working under AOT only while the
  trim-root stays; replacing the Newtonsoft-based ECS restart-state pipeline with source-generated
  serialization would remove that dependency and shrink binaries.
- Internal services/systems cannot participate in static hosts; module authors must keep graph
  members public or stay Dynamic-only.
- Publishes require the Visual Studio C++ toolchain; note `vcvars64.bat` replaces `PATH`
  (System32 must be re-appended) and exports `Platform=x64` (must be cleared for test layouts).

## Validation

- Gated `Static_server_host_publishes_and_runs_under_NativeAot_with_ten_reload_cycles` — win-x64
  AOT publish, startup, editor-snapshot round-trip, ten reload cycles, linear-only state growth,
  no orphan processes or locked files.
- Gated `Static_client_host_publishes_and_runs_under_NativeAot` — window creation, graphics backend
  init, input init, first rendered frame marker, clean shutdown.
- Generator parity tests: `GeneratedStaticRegistration_MatchesAttributeDiscovery_ServiceAndSystemSets`,
  `GeneratedStaticRegistration_MatchesDynamicDiscovery_ModuleIdSets` (Client and Server).
- Source-boundary tests pinning the AOT-safe serialization and system-visibility contracts.
- Full suites green after the default flip: Tasks 79, Runner 126, Network.Codegen 24, Core.Generator 39,
  ungated SDK integration 7.
