# Stabilize the module graph

This ExecPlan is a living document. It must be maintained according to `plans/PLANS.md`.

## Purpose / Big Picture

Replace the current filename-only Configurator prototype with a deterministic module graph that fails before runtime when module configuration is inconsistent.

Module projects and game projects use `KarpikModuleDependency` identifiers as their only source-level project-dependency item. MSBuild resolves identifiers through tracked `Generated/KarpikModuleCatalog.props` and expands them into standard `ProjectReference` items during project evaluation, so Rider and the compiler see referenced types immediately after project reload. Running Configurator is not required after adding a dependency to an existing project id; adding or moving projects requires regenerating the tracked catalog.

Configurator derives module identity, side, and implementation relationships from repository conventions instead of duplicating them in `.csproj` metadata. It uses the same `KarpikModuleDependency` items to validate enabled modules, compute DLL closure, topologically sort load order, and generate the loader.

## Progress

- [x] (2026-05-31 20:58 +04:00) Initial design discussion completed and decisions recorded.
- [x] (2026-05-31 21:08 +04:00) Add MSBuild expansion for `KarpikModuleDependency`.
- [x] (2026-05-31 21:08 +04:00) Migrate module and game project dependencies.
- [x] (2026-05-31 21:08 +04:00) Validate Milestone 1 from the command line: no direct `ProjectReference` remains under `Modules` or `MyGame`; evaluated analyzer metadata is forwarded; `ECS.Core`, `Graphics.OpenGL`, `MyGame.Client.Main`, and `MyGame.Server.Main` build successfully with `-m:1 -nr:false`. Rider project reload remains a manual IDE check.
- [x] (2026-05-31 21:24 +04:00) Extract Configurator model, parsing, validation, and generation.
- [x] (2026-05-31 21:24 +04:00) Migrate the game profile and regenerate loader artifacts.
- [x] (2026-05-31 21:24 +04:00) Stabilize installer init and destroy order.
- [x] (2026-05-31 21:24 +04:00) Add tests and run acceptance validation. `Configurator.Tests` passed 7/7, runner lifecycle tests passed 4/4, full solution tests passed 53/53, both launcher builds passed, and repeated generation produced byte-identical SHA256 hashes. Rider project reload remains a manual IDE check.
- [x] (2026-05-31 21:47 +04:00) Replace dependency paths with shorthand project ids, generate `Generated/KarpikModuleCatalog.props`, validate metadata forwarding, and rerun acceptance validation. `Configurator.Tests` passed 9/9, full solution tests passed 55/55, both launcher builds passed, and all three generated artifacts were byte-identical across repeated generation.

## Surprises & Discoveries

- Observation: `Configurator.LoadConfig` and `Configurator.SaveConfig` are stubs, so the existing `Enable*` and `Impl*` properties in `Directory.Build.props` do not affect generated artifacts.
  Evidence: `Configurator/Program.cs`.

- Observation: Existing profile values are stale: `ImplGraphics` selects removed backend `Raylib`, while generated loader currently loads `Graphics.OpenGL`; `EnableUIToolkit` has no matching project.
  Evidence: `Directory.Build.props` and `Generated/ModuleLoader.cs`.

- Observation: Filename-only scanning currently risks treating `Graphics.Core.Tests` and `Modding.TestModule` as runtime implementations.
  Evidence: `KarpikEngine.slnx` and `Configurator/Program.cs`.

- Observation: Compile dependencies and installer lifecycle dependencies are related but not identical. Existing priority values intentionally allow installer order to differ from compile order.
  Evidence: `AssetManagementInstaller`, `WindowSdlInstaller`, and their project references.

- Observation: `Generated/ModuleLoader.cs` is compiled into both `Karpik.Engine.Core` and `Karpik.Engine.Core.Runner`, causing existing `CS0436` conflicts.
  Evidence: targeted runner test build.

- Observation: The `KarpikModuleDependency` expansion must live in `Directory.Build.targets`, not `Directory.Build.props`. Props are imported before project-local items are declared, while targets are imported after the project body and still participate in evaluated IDE and compiler references.
  Evidence: MSBuild import order and `dotnet msbuild Modules\Shared\StatAndAbilities\StatAndAbilities.csproj -getItem:KarpikModuleDependency -getItem:ProjectReference -m:1 -nr:false`.

- Observation: Representative builds that share dependency outputs must run sequentially even with `-m:1`; launching independent `dotnet build` processes in parallel can race on common output DLLs.
  Evidence: parallel `Graphics.OpenGL` and `MyGame.Client.Main` validation raced on `Window.Core.dll`; the sequential `Graphics.OpenGL` rerun succeeded.

- Observation: `Modules/Shared/StatAndAbilities/StatAndAbilities.csproj` referenced a non-existent sibling `Modules/Shared/StatAndAbilities.Codegen` directory instead of the repository-root codegen project.
  Evidence: first graph-validator run after parser extraction; corrected dependency path resolves to `StatAndAbilities.Codegen/StatAndAbilities.Codegen/StatAndAbilities.Codegen.csproj`.

- Observation: Once the `StatAndAbilities.Codegen` analyzer path was corrected, launcher staging exposed stale generated-template imports of `Karpik.StatAndAbilities`; the runtime API namespace is `Karpik.Engine.Shared.StatAndAbilities`.
  Evidence: `ClientLauncher` build errors from generated `DefaultStat` and `DefaultRangeStat` sources; corrected in all three StatAndAbilities generator templates.

- Observation: MSBuild evaluation-time item functions do not provide a reliable join from dependency ids to discovered project items. A tracked generated catalog is simpler, deterministic, and preserves dependency metadata during the final item transform.
  Evidence: prototype `WithMetadataValue` expansion left `ResolvedProjectPath` empty; generated `KarpikModuleCatalog.props` resolves ids and `dotnet msbuild ... -getItem:ProjectReference` confirms forwarded analyzer metadata.

## Decision Log

- Decision: Do not add explicit role, side, module id, or plugin id metadata to module projects.
  Rationale: These values are derivable from project path and project name. Duplicating them creates drift without adding information.
  Date/Author: 2026-05-31 / developer and agent

- Decision: Do not add module versioning in this milestone.
  Rationale: There is no external module packaging or compatibility negotiation yet.
  Date/Author: 2026-05-31 / developer

- Decision: In `Modules` and `MyGame`, use `KarpikModuleDependency` as the only source-level project dependency item, including infrastructure, third-party, analyzer, and codegen references.
  Rationale: One dependency declaration keeps IDE references and module-graph validation aligned. Standard `ProjectReference` remains an internal evaluated MSBuild representation.
  Date/Author: 2026-05-31 / developer

- Decision: Preserve `[Module(priority)]` for lifecycle ordering and add deterministic tie-breaking. Do not replace it with compile-DAG order.
  Rationale: Existing installers contain intentional priority overrides. Compile dependency does not automatically imply service-registration order.
  Date/Author: 2026-05-31 / developer and agent

- Decision: Destroy installers in reverse init order.
  Rationale: Teardown must release consumers before providers.
  Date/Author: 2026-05-31 / developer and agent

- Decision: Source-level `KarpikModuleDependency` values are project ids, not relative paths. Logical module ids such as `Physics2D` resolve to their core plugin; implementation ids such as `Physics2D.Aether2D` resolve to the concrete plugin; infrastructure ids such as `Dragon` resolve by project filename.
  Rationale: Project files stay compact and project moves no longer require rewriting dependent `.csproj` files. Compile dependencies remain independent from the selected runtime implementation.
  Date/Author: 2026-05-31 / developer and agent

- Decision: Generate and track `Generated/KarpikModuleCatalog.props`.
  Rationale: The catalog keeps MSBuild evaluation and Rider design-time resolution deterministic without recursive filesystem scans or unreliable MSBuild item joins.
  Date/Author: 2026-05-31 / agent

## Outcomes & Retrospective

The filename-only Configurator prototype has been replaced with a deterministic validated module graph. `KarpikModuleDependency` identifiers are the only source-level dependency declarations under `Modules` and `MyGame`; MSBuild resolves them through a generated catalog during evaluation for IDE and compiler compatibility. The structured profile lists every logical module explicitly, generated artifacts are deterministic and validated as read-only during launcher builds, and the loader is compiled only into `Karpik.Engine.Core`.

Installer lifecycle is now deterministic for equal priorities and destroys consumers before providers by reversing init order. The migration exposed and fixed an existing broken `StatAndAbilities.Codegen` path plus stale generated-template namespaces.

Accepted architecture decision: [[../docs/02_ADR/module-graph]].

## Context and Orientation

`Configurator/RepositoryParser.cs` scans `KarpikEngine.slnx` and raw project XML. `Configurator/ArtifactGenerator.cs` writes `AutoGenerated.targets`, `Generated/KarpikModuleCatalog.props`, and `Generated/ModuleLoader.cs`.

`Directory.Build.targets` invokes `Configurator --validate` before launcher and publish builds. Extend this existing validation path rather than adding a second build-time tool.

`Generated/KarpikModuleCatalog.props` is a tracked generated artifact imported by `Directory.Build.targets`. It maps dependency ids to project paths during evaluation. `Generated/ModuleLoader.cs` stages and loads DLLs before `EngineRunner` discovers installer types.

`Karpik.Engine.Core.Runner/Runner.cs` sorts installers by `[Module(priority)]`, then runs service registration and configuration callbacks. It currently destroys modules in forward order.

## Real-Time Assessment

The graph work runs during MSBuild evaluation, Configurator execution, startup, and shutdown. It must not add frame-loop, fixed-tick, ECS-run, render-loop, or network-pump work.

MSBuild items and Configurator object graphs may allocate because they execute outside hot paths. Runtime loader descriptors may allocate during startup only. The runtime update path must remain unchanged.

The existing Client / Server / Shared boundary rules remain mandatory. Validation must reject side leaks before runtime.

## Conventions

### Engine modules

Every library project under `Modules/{Client|Server|Shared}` is a plugin candidate.

- Side is derived from the first path segment below `Modules`.
- Plugin id is the `.csproj` filename without extension.
- The containing project directory must match plugin id.
- Explicit `AssemblyName`, when present, must match plugin id.
- `X.Core` is the core plugin of logical module `X`.
- `X.Backend` is implementation `Backend` of logical module `X` when `X.Core` exists.
- A project without a matching `X.Core` is a standalone logical module.
- Projects with `IsTestProject=true` and executable projects are excluded.

Examples:

| Project | Logical module | Plugin kind |
| --- | --- | --- |
| `Modules/Client/Input/Input.csproj` | `Input` | standalone |
| `Modules/Shared/ECS/ECS.Core/ECS.Core.csproj` | `ECS` | core-only |
| `Modules/Client/Graphics/Graphics.OpenGL/Graphics.OpenGL.csproj` | `Graphics` | implementation `OpenGL` |
| `Modules/Shared/Network.Shared.LiteNetLib/Network.Shared.LiteNetLib.csproj` | `Network.Shared` | implementation `LiteNetLib` |

### Game roots

Treat these as always-enabled roots outside the selectable engine-module list:

- `MyGame/MyGameResources/MyGameResources.csproj`, shared;
- `MyGame/Shared/MyGame.Shared.Main/MyGame.Shared.Main.csproj`, shared;
- `MyGame/Client/MyGame.Client.Main/MyGame.Client.Main.csproj`, client;
- `MyGame/Server/MyGame.Server.Main/MyGame.Server.Main.csproj`, server.

Reject unknown game projects until a convention is explicitly added for them.

## Dependency Contract

### Source-level item

Module and game projects declare all project dependencies with one item type:

```xml
<ItemGroup>
  <KarpikModuleDependency Include="Graphics" />
</ItemGroup>
```

The same item is used for infrastructure, third-party, analyzer, and codegen projects:

```xml
<KarpikModuleDependency
    Include="StatAndAbilities.Codegen"
    OutputItemType="Analyzer"
    ReferenceOutputAssembly="false" />
```

Optional runtime-only dependency:

```xml
<KarpikModuleDependency
    Include="DebugModule"
    Optional="true"
    ReferenceOutputAssembly="false" />
```

### MSBuild expansion

Import the generated catalog and expand declared dependencies during evaluation:

```xml
<ItemGroup>
  <ProjectReference Include="@(KarpikModuleDependency->'%(ResolvedProjectPath)')" />
</ItemGroup>
```

Preserve item metadata such as `OutputItemType`, `ReferenceOutputAssembly`, `ExcludeAssets`, and conditions. Verify Rider design-time resolution and command-line builds after introducing the rule.

Configurator derives catalog ids from solution projects. A module core plugin such as `Physics2D.Core.csproj` also receives logical alias `Physics2D`. Concrete implementations and infrastructure projects use their `.csproj` filename without extension. Unknown and ambiguous ids fail validation.

Do not hand-write `ProjectReference` inside project files under `Modules` or `MyGame`. Configurator validation must reject direct declarations there.

### Graph semantics

Configurator reads raw `KarpikModuleDependency` declarations from project XML.

- A dependency targeting `Modules` or `MyGame` adds a module-graph edge.
- A dependency targeting infrastructure or third-party code affects compilation but does not add a module-graph node.
- A required dependency targeting a disabled module fails validation.
- An optional dependency targeting a disabled module is ignored without warning.
- An optional dependency targeting an enabled module participates in topological sorting.
- An optional dependency cannot be used for compile-time type access when its target may be disabled.

## Profile And Schema

Replace stale `Enable*` and `Impl*` properties in `Directory.Build.props` with structured items:

```xml
<ItemGroup Label="ProjectConfigurator">
  <KarpikModuleSelection Include="Graphics"
                         Enabled="true"
                         Implementation="OpenGL" />
  <KarpikModuleSelection Include="Input"
                         Enabled="true" />
</ItemGroup>
```

Require one selection for every engine logical module, including disabled modules. Game roots are implicit and do not need selection. A new engine module without a selection fails validation instead of silently changing runtime composition.

Support schema declarations in module `.csproj` files:

```xml
<KarpikModuleSetting Include="ColorSrgb"
                     Type="Boolean"
                     Default="true" />
```

Support `String`, `Boolean`, `Int32`, `Double`, and `Enum`, including defaults, required values, and allowed enum values. Store overrides in `Directory.Build.props` as `KarpikModuleSettingValue`. Do not add production settings or runtime binding in this milestone.

## Plan of Work

### Milestone 1: Introduce the dependency item

Add the MSBuild expansion rule and migrate dependencies used by projects under `Modules` and `MyGame`. Convert shared injected analyzer/codegen references that apply to these projects so source-level dependency declarations consistently use `KarpikModuleDependency`.

Validate representative module builds and verify that Rider reload resolves referenced types without running Configurator.

### Milestone 2: Extract and validate the graph

Split `Configurator/Program.cs` into pure model, parser, validator, generator, and console-UI responsibilities.

Read conventions from solution projects and raw XML. Read `KarpikModuleDependency` without relying on evaluated `ProjectReference`, otherwise direct-reference misuse cannot be diagnosed reliably.

Validate naming, candidates, selections, settings, side boundaries, required dependencies, optional dependencies, missing projects, duplicate ids, and cycles. Topologically sort each active side graph deterministically by plugin id when multiple nodes are ready.

### Milestone 3: Generate deterministic artifacts

Generate `AutoGenerated.targets`, `Generated/KarpikModuleCatalog.props`, and `Generated/ModuleLoader.cs` only after full validation.

Keep `--validate` read-only: build expected content in memory, compare it to tracked generated artifacts, and fail with a command hint when files are stale.

Keep C# loader as the only generated manifest. Do not add JSON. Include startup-only descriptors for diagnostics plus computed Client and Server load arrays.

Compile loader only into `Karpik.Engine.Core`. Remove duplicate compilation from `Karpik.Engine.Core.Runner` and generated targets.

### Milestone 4: Stabilize installer lifecycle

Keep priority as the primary lifecycle order. Add deterministic tie-breaking with assembly load rank and installer full type name. For manual test registration, retain registration rank as the fallback.

Use the resulting order consistently for register/configure/complete/listener callbacks. Destroy `IInstallerDestroy` instances in reverse init order.

### Milestone 5: Test and document

Add `Configurator.Tests` as an xUnit project in `KarpikEngine.slnx`. Expand runner lifecycle tests. Update roadmap checkboxes as milestones complete and create or update an ADR before closing this plan.

## Validation And Acceptance

Add Configurator tests for:

- convention inference for side, standalone, core, and implementation projects;
- exclusion of tests and executable projects;
- rejection of invalid project directory, explicit assembly name, and direct `ProjectReference`;
- metadata forwarding from `KarpikModuleDependency` to evaluated `ProjectReference`;
- profile load/save and missing selections;
- required and optional dependencies;
- infrastructure dependency exclusion from module graph;
- Client / Server / Shared boundary leaks;
- missing dependencies, duplicate ids, and cycle diagnostics;
- schema defaults and invalid values;
- deterministic topological order;
- stale generated artifact detection;
- game-root inclusion for both sides.

Expand runner tests for:

- deterministic sorting when priorities are equal;
- explicit priority precedence;
- reverse destroy order.

Run:

```powershell
dotnet test Configurator.Tests\Configurator.Tests.csproj -m:1 -nr:false
dotnet test Karpik.Engine.Core.Runner.Tests\Karpik.Engine.Core.Runner.Tests.csproj -m:1 -nr:false
dotnet run --project Configurator\Configurator.csproj -- --generate
dotnet run --project Configurator\Configurator.csproj -- --validate
dotnet test KarpikEngine.slnx -m:1 -nr:false
dotnet build ClientLauncher\ClientLauncher.csproj -m:1 -nr:false
dotnet build ServerLauncher\ServerLauncher.csproj -m:1 -nr:false
```

Expected observations:

- Rider resolves referenced module types after project reload without running Configurator.
- No direct `ProjectReference` remains in source project files under `Modules` or `MyGame`.
- Client and Server launcher builds pass validation.
- Existing `CS0436` loader conflict is removed.
- Generated loader order is deterministic across repeated generation.

## Idempotence And Recovery

`--generate` must be safe to rerun and must produce byte-identical output for unchanged inputs.

Perform migration milestone-by-milestone. If MSBuild expansion breaks design-time or command-line resolution, revert only the expansion and migrated dependency declarations from the current milestone; generated runtime artifacts remain usable until migration resumes.

Do not run formatting across the entire solution as part of this task. Use targeted builds and tests.

## Artifacts And Notes

- Active plan: `plans/module-graph-execplan.md`
- Existing generator: `Configurator/Program.cs`
- Generated artifacts: `AutoGenerated.targets`, `Generated/KarpikModuleCatalog.props`, `Generated/ModuleLoader.cs`
- Build hook: `Directory.Build.targets`
- Runtime lifecycle: `Karpik.Engine.Core.Runner/Runner.cs`
