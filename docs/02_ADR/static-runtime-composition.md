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
   STATUS: re-pinned 2026-08-25 to the exact MEMBER-level multiset emitted by the
   first gated publishes under `-p:TrimmerSingleWarn=false` (Server and Client both
   PASS). The authoritative list lives in `StaticCompositionCliTests`
   (`SharedDocumentedAotWarnings` — 105 tuples shared by Server and Client;
   `ClientOnlyDocumentedAotWarnings` — 6 Silk.NET/DependencyModel members); the
   summary below groups those members per origin with their justification.

   **Third-party payload members (Server and Client):**

   | Origin | Codes (multiplicity) | Justification |
   |---|---|---|
   | `Newtonsoft.Json` (`JToken`/`JObject`/`JValue`/`JContainer`/`Json.Schema`) | IL2026 ×7, IL3050 ×6 | reflection serializer backing the hot-reload state pipeline |
   | `nkast.Aether.Physics2D` (`WorldXmlSerializer`/`WorldXmlDeserializer`) | IL2026 ×4, IL2057 ×1, IL3050 ×4 | ships unannotated; internal XmlSerializer world (de)serialization |
   | `MoonSharp.Interpreter` (`TableConversions`, `FrameworkClrBase`, `ValueTypeDefaultCtorMemberDescriptor`, `ExtensionMethodsRegistry`, `StandardEnumUserDataDescriptor`) | IL2055 ×2, IL2060 ×1, IL2067 ×3, IL2072 ×2, IL2075 ×10, IL3050 ×4 | Lua interpreter is inherently reflective |
   | `DCFApixels.DragonECS` (`TypeMeta`, `EcsDebugUtility`, `JsonDebugger`) | IL2055 ×1, IL2070 ×1, IL2075 ×2, IL2077 ×2, IL3050 ×1 | ships unannotated debug/meta reflection surface |
   | `Microsoft.CSharp` (`ComObject.RcwToComObject`) + `System.Linq.Expressions` (`CachedReflectionInfo.DynamicObject_*.get` ×12) | IL3050 ×13 | BCL dynamic-code surfaces reachable only through the rooted third-party metadata above; not exercised on the static path |

   **First-party members (Server and Client, deliberate):**

   | Origin | Codes (multiplicity) | Justification |
   |---|---|---|
   | `Karpik.Engine.Core.Runner` / `Karpik.Engine.Core` / `ModuleLoader` (`DynamicCompositionDiscovery`, `Program.<LoadDynamicModules>`, `Bootstrap.RegisterTypes`, `AttributedServiceRegistrar`, `SystemRegistry`, `EngineRunner.FormatComponent`, `LoadPrimaryAssembly`) | IL2026 ×3, IL2062 ×1, IL2067 ×1, IL2072 ×3, IL2075 ×2 | Dynamic-mode-only discovery paths analyzed by ilc but unreachable from a generated static host; deletion is a tracked ExecPlan follow-up |
   | ECS state pipeline (`ComponentArrayConverter`, `EcsWorldExtensions`) | IL2026 ×6, IL3050 ×6 | Newtonsoft restart-state snapshots; replacement tracked as the source-generated ECS state serialization follow-up ExecPlan; behavior proven by the ten reload cycles of the Server gate |
   | Component-template fallback (`ToTemplateExtensions[2]`, `ComponentTemplateBase`1.DefaultValueType.get`) | IL2070 ×2, IL2076 ×2, IL2090 ×2, IL3050 ×2 | `MakeGenericType`/`GetFields` fallback of `ComponentTemplate<T>`; surfaced at member level as IL2090 trim-annotation warnings inside the same documented fallback surface — runtime behavior proven by the Server gate reload cycles |
   | Aspect scheduling reflection (`SystemExecutionNode.GetAspectTypes`) | IL2075 ×2 | aspect-type construction over open generics, part of the documented ECS reflection surface |
   | `AssetManagement.Core` (`LooseAssemblyNameBinder`, `JsonLoader`2`, `JsonSaver`1`) | IL2026 ×4, IL2057 ×1, IL3050 ×3 | Newtonsoft content pipeline + loose assembly binding (`Assembly.GetTypes`, `Type.GetType`) |
   | `LoggerModuleInstaller.OnRegisterServices` | IL3050 ×1 | open-generic Autofac logger registration (Dynamic-mode DI path) |

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
