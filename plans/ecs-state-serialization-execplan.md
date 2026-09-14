# Перевести ECS restart-state на generated binary codecs

This ExecPlan is a living document. It must be maintained according to `plans/PLANS.md`.

## Purpose / Big Picture

Static/NativeAOT worker reload должен сохранять три ECS world без Newtonsoft `TypeNameHandling`, runtime type binding и assembly-wide metadata roots. После выполнения плана `EcsRestartWorkerStateProvider` пишет версионированный бинарный envelope через сгенерированный registry component codecs, новый worker восстанавливает те же entity IDs и component values, а Server NativeAOT gate проходит десять reload cycles без roots для `Newtonsoft.Json`, `DragonECS`, `ECS.Core` и game component assemblies.

Observable result: template Server сохраняет `GameComponent=42` и стабильный state payload через десять process-isolated reload cycles; несовместимый schema hash отклоняется до изменения world; trimmed publish не содержит необъяснённых IL warnings и не требует state-pipeline roots.

## Progress

- [x] (2026-08-25) Initial follow-up plan extracted from `plans/static-composition-nativeaot-execplan.md` after Milestone 9 audit.
- [ ] Milestone 1: pin the existing restart-state compatibility contract and generated codec model with failing tests.
- [ ] Milestone 2: generate deterministic component IDs, schema hash and typed read/write codecs.
- [ ] Milestone 3: replace the Newtonsoft inner snapshots and preserve atomic restore behavior.
- [ ] Milestone 4: remove obsolete converters/roots and pass Server/Client NativeAOT acceptance.

## Surprises & Discoveries

- Observation: the outer `HotReloadInfo` envelope already uses `System.Text.Json` source generation, but each of its three world fields is still a Newtonsoft JSON string.
  Evidence: `Modules/Shared/ECS/ECS.Core/EcsRestartWorkerStateProvider.cs` calls `world.ToSnapshot(_converter)` and serializes the resulting strings through `HotReloadInfoJsonContext`.
- Observation: `EcsWorldExtensions.ToSnapshot` materializes `List<EntitySnapshot>`, `List<object>`, arrays and formatted JSON; it is reload-time work rather than a frame hot path, but payload size and peak allocation are unnecessarily high.
  Evidence: `Modules/Shared/ECS/ECS.Core/EcsWorldExtensions.cs` uses `JsonConvert`, `TypeNameHandling.Objects` and `Formatting.Indented`.
- Observation: component restoration currently resolves `$type` strings through a serialization binder and non-generic `JObject.ToObject(Type, JsonSerializer)`.
  Evidence: `Modules/Shared/ECS/ECS.Core/AssetManagement/Converters/ComponentArrayConverter.cs`.

## Decision Log

- Decision: use a generator-owned binary registry keyed by deterministic 64-bit component IDs; do not preserve CLR type names in the new payload.
  Rationale: NativeAOT needs statically reachable generic instantiations, and stable IDs remove runtime type binding and assembly-name coupling.
  Date/Author: 2026-08-25 / AI assistant, following the accepted static-composition ADR.
- Decision: make the payload fail-closed with magic, format version, side/schema hash, world count and bounded lengths before mutating any ECS world.
  Rationale: a partially compatible or truncated reload payload must not destroy the currently initialized world or deserialize into the wrong component layout.
  Date/Author: 2026-08-25 / AI assistant.
- Decision: keep `IRestartWorkerStateProvider.Capture(): byte[]` and `Restore(ReadOnlySpan<byte>)` unchanged in this plan.
  Rationale: the process/IPC boundary is already stable; only the ECS provider payload changes.
  Date/Author: 2026-08-25 / AI assistant.

## Outcomes & Retrospective

Implementation has not started. Milestone results, measured payload sizes, allocation observations and final NativeAOT warning deltas must be recorded here before closing the plan.

## Context and Orientation

`Karpik.Engine.Core/IRestartWorkerStateProvider.cs` is the process-restart persistence boundary. `Karpik.Engine.Core.Runner/Runner.cs` captures all providers before worker replacement and restores them before system initialization.

`Modules/Shared/ECS/ECS.Core/EcsRestartWorkerStateProvider.cs` owns ECS state for `EcsDefaultWorld`, `EcsEventWorld` and `EcsMetaWorld`. It currently delegates inner snapshots to `Modules/Shared/ECS/ECS.Core/EcsWorldExtensions.cs`. That extension serializes `EntitySnapshot` and `ComponentsTemplate` through Newtonsoft and `ComponentArrayConverter`, which embeds CLR `$type` names and resolves them at runtime.

`Karpik.Engine.Core.Generator/Karpik.Engine.Core.Codegen/RuntimeCompositionGenerator.cs` already discovers referenced ECS component structs and emits AOT generic-instantiation roots. Extend that model rather than adding a second reflection discovery mechanism. Generated runtime composition is side-specific, so Client and Server schemas may differ but each payload must carry and validate the exact generated schema hash.

The new binary format is internal restart state, not the network snapshot protocol. Do not reuse network component IDs unless an explicit shared contract is introduced and collision behavior remains a build error.

## Real-Time Assessment

Capture and restore run only at the process-reload boundary, not in `Update`, `FixedUpdate`, ECS `Run`, the network pump or render loop. Some bounded allocation is acceptable, but the implementation must avoid per-field reflection, `object` component arrays and formatted JSON. Prefer a pre-sized `ArrayBufferWriter<byte>` or equivalent owned buffer for capture and span-based reading for restore.

ECS storage and frame processing remain unchanged. Component codecs operate on typed `ref` pool access and deterministic entity iteration. No Client project may enter Server/Shared dependencies and no Server project may enter Client/Shared dependencies. Restore performs no blocking I/O and introduces no locks in game-loop code.

## Plan of Work

First pin the current observable behavior: entity IDs, enabled/disabled component state, default/event/meta worlds, empty worlds, unknown schema, truncated payload and restore-before-init ordering. Add a golden legacy snapshot only as a migration fixture if one-release backward compatibility is explicitly required; otherwise reject the old JSON format with a precise version error.

Extend `RuntimeCompositionGenerator` with a `RestartStateComponentModel` derived from the existing component discovery. For every concrete `struct` implementing the ECS component contract, emit a deterministic component ID, a typed writer and a typed reader. Support fields recursively only when their serialization is deterministic and AOT-safe: primitives, enums, fixed supported engine value structs and explicitly generated component structs. Emit a build diagnostic for managed references, pointers, unsupported generic fields or ID collisions.

Add a generated `IRestartStateCodecRegistry` implementation to `IStaticRuntimeComposition`. The registry writes a fixed header, schema hash, three world records, entity IDs and sorted component records. Each component record is length-delimited so validation can reject malformed data without reading beyond the payload. Restore first validates the complete envelope and schema, decodes into temporary restart records, then clears and repopulates worlds; failure before commit leaves initialized worlds unchanged.

After the generated path is green, remove `ComponentArrayConverter`, `LooseAssemblyNameBinder` and the Newtonsoft snapshot calls from the restart path. Keep asset JSON converters only if independent asset loading still uses them. Narrow SDK trim roots one assembly at a time and let the member-level warning gate expose any remaining reflection dependency.

## Milestones

### Milestone 1: Compatibility and failure contract

Add tests under `ECS.Core.Tests` and `Karpik.Engine.Core.Runner.Tests` for empty and populated three-world round trips, preserved entity IDs, restore-before-system-init, schema mismatch, unknown format version, duplicate component records, truncated lengths and no partial world mutation. Run:

    dotnet test ECS.Core.Tests/ECS.Core.Tests.csproj -m:1 -nr:false
    dotnet test Karpik.Engine.Core.Runner.Tests/Karpik.Engine.Core.Runner.Tests.csproj -m:1 -nr:false --filter "FullyQualifiedName~RestartState|FullyQualifiedName~AutofacComposition"

Expected: new behavioral tests fail only because the generated binary registry does not yet exist; existing tests remain green.

### Milestone 2: Generated component codecs

Modify `RuntimeCompositionGenerator.cs`, its model files if extracted, `IStaticRuntimeComposition` and generator tests. Add diagnostics for unsupported fields and component ID collisions. Add literal expected-byte tests for at least one component containing integer, floating-point, enum and nested engine value fields. Add a generator-driver incremental test proving unrelated host edits do not rebuild the referenced component model.

Run:

    dotnet test Karpik.Engine.Core.Generator.Tests/Karpik.Engine.Core.Generator.Tests.csproj -m:1 -nr:false

Expected: all generator tests pass; emitted source contains direct typed codec calls and no `Type.GetType`, `MakeGenericType`, `Activator`, Newtonsoft or object-valued serializer dispatch.

### Milestone 3: Atomic ECS world capture and restore

Modify `EcsRestartWorkerStateProvider.cs` and replace the restart-specific methods in `EcsWorldExtensions.cs`. Register the generated registry through static composition and inject it into the provider. Decode and validate into temporary records before clearing any world; apply components through typed pool operations in deterministic component-ID order.

Run the Milestone 1 commands plus:

    dotnet test Karpik.Engine.Sdk.IntegrationTests/Karpik.Engine.Sdk.IntegrationTests.csproj -m:1 -nr:false --filter "FullyQualifiedName~Static_server_host_runs_without_plugin_context_or_module_manifest"

Expected: all round trips pass, malformed/schema-mismatched payloads leave worlds unchanged, and the static host process test passes.

### Milestone 4: Remove reflection surface and prove NativeAOT

Remove restart-path usages of `ComponentArrayConverter`, `LooseAssemblyNameBinder`, `JsonConvert` and `TypeNameHandling`. Delete code only when repository-wide callers show it is restart-only. Remove the corresponding trim roots from `Karpik.Engine.Sdk/Sdk/Sdk.targets` and template launcher projects, then repin the exact member-level warning multiset in `StaticCompositionCliTests.cs` and `docs/02_ADR/static-runtime-composition.md`.

Run the complete targeted suites followed by both gated publishes:

    dotnet test Karpik.Engine.Sdk.Tasks.Tests/Karpik.Engine.Sdk.Tasks.Tests.csproj -m:1 -nr:false
    dotnet test Karpik.Engine.Core.Generator.Tests/Karpik.Engine.Core.Generator.Tests.csproj -m:1 -nr:false
    dotnet test Karpik.Engine.Core.Runner.Tests/Karpik.Engine.Core.Runner.Tests.csproj -m:1 -nr:false
    dotnet test Karpik.Engine.Sdk.IntegrationTests/Karpik.Engine.Sdk.IntegrationTests.csproj -m:1 -nr:false

Then run `Static_server_host_publishes_and_runs_under_NativeAot_with_ten_reload_cycles` and `Static_client_host_publishes_and_runs_under_NativeAot` with the repository's documented gated environment. Expected: both pass, Server payload size is stable after warm-up, no orphan or locked process artifacts remain, and no state-pipeline assembly root or unexplained IL warning remains.

## Concrete Steps

All commands run from `C:\Users\artem\RiderProjects\KarpikEngine`. Follow TDD for each milestone: add one failing behavioral test, record its expected failure in `Progress`, implement the smallest production change, rerun the targeted command, then update this document before continuing.

Use `rg` only for literal/config searches after CBM identifies candidate symbols. Before deleting converters or roots, run repository-wide caller and `PackageReference` checks. Build/test commands use `-m:1 -nr:false`.

## Validation and Acceptance

Acceptance requires all of the following:

1. Three ECS worlds round-trip entity IDs, component values and enabled state through the generated binary format.
2. Payload validation rejects wrong magic, version, schema, duplicate IDs and out-of-range lengths before changing a world.
3. Generated codecs contain no reflection activation, runtime generic construction, Newtonsoft dispatch or `object` component serialization.
4. Capture/restore allocation and payload-size measurements are recorded; no allocations are added to frame or ECS Run paths.
5. Ten process-isolated Server reloads preserve state with stable post-warm-up payload size and no orphan processes or locked files.
6. Server and Client NativeAOT gates pass without state-pipeline trim roots and without unexplained `IL2xxx`, `IL3xxx` or `IL3050` warnings.
7. ADR warning inventory and this plan's `Outcomes & Retrospective` match the verified final behavior.

## Idempotence and Recovery

Generated output is deterministic and safe to regenerate. Use a new format version while developing so an old worker cannot consume an incompatible payload. Restore validates into temporary records before world mutation, allowing retry or clean worker initialization after invalid state.

Do not remove the Newtonsoft implementation or trim roots until the generated path passes unit, integration and NativeAOT gates. Rollback is a normal commit revert of the new codec registration and provider switch; do not use `git reset --hard`. Preserve the failing regression fixtures if a NativeAOT dependency blocks completion.

## Artifacts and Notes

- Parent completed plan: `plans/static-composition-nativeaot-execplan.md`.
- Architecture decision: `docs/02_ADR/static-runtime-composition.md`.
- Current provider: `Modules/Shared/ECS/ECS.Core/EcsRestartWorkerStateProvider.cs`.
- Current reflection serializer: `Modules/Shared/ECS/ECS.Core/EcsWorldExtensions.cs` and `AssetManagement/Converters/ComponentArrayConverter.cs`.
- NativeAOT gate/report: `Karpik.Engine.Sdk.IntegrationTests/StaticCompositionCliTests.cs` and `.git/sdd/task-m9-report.md`.
