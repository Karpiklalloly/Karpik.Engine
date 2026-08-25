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
   - Static+AOT publishes root a minimal, justified set only
     (`_KarpikRootStaticGraphReflectionMetadata`): `ECS.Core`, `DragonECS`,
     `Newtonsoft.Json` (the reflection surface of the hot-reload state pipeline)
     and the game `ProjectReference` assemblies that define snapshot component
     structs. The blanket root over every installed module payload assembly was
     removed in Milestone 9; ECS scheduling registries are delivered through
     generated composition providers and component template instantiations
     through the generated `TouchAotComponentTemplateRoots` call, so module
     payload assemblies stay trimmed.

5. **Warning inventory (exact, gate-enforced).** Neither the SDK nor host projects set any
   `NoWarn`: every trim/AOT warning reaches the publish output. Both AOT acceptance gates parse
   the publish log into exact `(code -> originating assembly[/member])` tuples and FAIL unless
   the emitted MULTISET equals the documented multiset (count-sensitive per tuple; verified
   2026-08-24) — a package update that adds or removes origins, or adds a warning inside an
   already-documented origin, changes the multiplicity and fails the gate, forcing this inventory
   to be revisited. The gated publishes run with `-p:TrimmerSingleWarn=false` so ILC/trimmer emit
   individual warnings instead of per-assembly single-warn aggregates. Aggregate
   form (`ILxxxx: Assembly 'X' produced ... warnings.`) pins the originating assembly; member
   form (`ILxxxx: Ns.Type.Member(args): ...`) pins the member where ilc emits one. A coverage
   fallback compares per-code occurrence COUNTS between the catch-all `\bIL\d{4}\b` scan and the
   parsed tuples, so a warning in an unrecognized textual shape can never slip through.
   STATUS: the table below is still the AGGREGATE-form inventory pinned before
   `TrimmerSingleWarn=false` was adopted; the first gated publish under the flag will surface
   member-level warnings for these origins and this table MUST be re-pinned to that exact
   member-level form (which also strengthens the first-party entries with concrete members).

   **IL2104 + IL3053 aggregates (Server and Client):**

   | Origin | Kind | Justification |
   |---|---|---|
   | `Aether.Physics2D` | third-party payload | ships unannotated; internal XmlSerializer world (de)serialization |
   | `MoonSharp.Interpreter` | third-party payload | Lua interpreter is inherently reflective |
   | `Newtonsoft.Json` | third-party payload | reflection serializer backing the hot-reload state pipeline |
   | `DragonECS` | third-party payload | ships unannotated |
   | `ECS.Core` | FIRST-PARTY, deliberate | Newtonsoft restart-state snapshots (`ComponentArrayConverter`, `EcsWorldExtensions`) + `ComponentTemplate<T>` `MakeGenericType` fallback; replacement tracked as the source-generated ECS state serialization follow-up ExecPlan; behavior proven by the ten reload cycles of the Server gate |
   | `AssetManagement.Core` | FIRST-PARTY, deliberate | Newtonsoft `JsonLoader`2`/`JsonSaver`1` content pipeline + `LooseAssemblyNameBinder` loose assembly binding (`Assembly.GetTypes`, `Type.GetType`) |
   | `Karpik.Engine.Core` | FIRST-PARTY, dead under Static | Dynamic-mode-only discovery (`Bootstrap.RegisterTypes`, `EngineRunner.FormatComponent`, `AttributedServiceRegistrar`, `SystemRegistry`) analyzed by ilc but unreachable from a generated static host |
   | `Karpik.Engine.Core.Runner` | FIRST-PARTY, dead under Static | same: `DynamicCompositionDiscovery`, `Program.LoadDynamicModules` (`Assembly.GetTypes`, `Activator.CreateInstance`) |

   **IL3053 aggregates (Server and Client, additional):** `LoggerModule` (open-generic Autofac
   logger registration — Dynamic-mode DI path), `Microsoft.CSharp`,
   `System.Linq.Expressions` (BCL dynamic-code surfaces reachable only through the rooted
   third-party metadata above; not exercised on the static path).

   **IL3000/IL3002 members (Client only):** `Silk.NET.Core.Loader.DefaultPathResolver`
   (`.<>c.<.cctor>b__24_3(String)`, `.TryLocateNativeAssetFromDeps(String,String&,String&)`,
   `.TryLocateNativeAssetInRuntimesFolder(String,String,String&)`) plus
   `Microsoft.Extensions.DependencyModel.DependencyContext..cctor()`: Silk.NET probes native
   dependency paths via `Assembly.Location`/`Assembly.CodeBase`/`DependencyContext`, which are
   empty/unsupported under single-file AOT; installation natives are staged next to the
   executable so resolution succeeds regardless — justified empirically by the passing runtime
   gate.

   The first-party entries stay whitelisted because fixing them at source means replacing the
   Newtonsoft state pipeline or deleting the Dynamic-mode discovery code paths — both tracked,
   ExecPlan-sized follow-ups; none is silently suppressed. Any origin outside this table fails
   the publish gate.

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
  no orphan processes or locked files; publish warnings must equal the Decision-5 inventory
  exactly (code -> origin tuples).
- Gated `Static_client_host_publishes_and_runs_under_NativeAot` — window creation, graphics backend
  init, input init, first rendered frame marker, clean shutdown; same exact warning-inventory
  assertion with the client additions (Silk.NET IL3000/IL3002 members).
- Generator parity tests: `GeneratedStaticRegistration_MatchesAttributeDiscovery_ServiceAndSystemSets`,
  `GeneratedStaticRegistration_MatchesDynamicDiscovery_ModuleIdSets` (Client and Server).
- Source-boundary tests pinning the AOT-safe serialization and system-visibility contracts.
- Full suites green after the default flip: Tasks 79, Runner 126, Network.Codegen 24, Core.Generator 39,
  ungated SDK integration 7.
