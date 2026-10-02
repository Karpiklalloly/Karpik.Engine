---
title: "Versioned engine SDK and external game projects"
date: "2026-07-15"
status: "accepted"
tags:
  - adr
  - architecture
  - editor
  - sdk
  - tooling
---

# Versioned engine SDK and external game projects

> Status: accepted
> Date: 2026-07-15
> Owners: KarpikEngine developers

## Context

The repository currently acts as the engine source tree, editor workspace, module catalog, sample game, launcher source, and runtime-bundle root at the same time. `Karpik.Editor` can persist a project path, but its client and server runtime bundles are built into the editor output. Shared MSBuild files and Configurator also assume that `Modules` and `MyGame` are below the KarpikEngine repository root.

This layout is suitable for developing one engine checkout and its embedded sample, but not for keeping several independent games on disk. A game cannot pin an installed engine version, move to a separate repository, or build without inheriting the physical layout of the engine source tree.

The project model must meet these constraints:

- games can live anywhere and in repositories separate from KarpikEngine;
- a game normally consumes an installed, exact engine SDK version;
- engine developers can build an equivalent local SDK from source and use it as an override;
- normal `dotnet restore`, `dotnet build`, `dotnet test`, and `dotnet publish` commands work without starting the editor;
- the editor owns exactly one active game project while still supporting one server and multiple client runtime sessions for that game;
- Client, Server, and Shared boundaries remain validated;
- project discovery does not require a custom `.karpik` manifest or repository-level `Directory.Build.props` contract;
- project and SDK validation add no work or allocation to the frame, fixed-tick, ECS, render, or network hot paths.

## Decision

### Game projects use a custom MSBuild SDK

Every `.csproj` included in a game `.slnx` must use `Karpik.Engine.Sdk`. The SDK composes the normal .NET SDK and supplies Karpik build targets, analyzers, generators, module resolution, side validation, runtime-bundle construction, asset-pipeline integration, and publishing behaviour.

Each project declares its kind and side independently:

```xml
<Project Sdk="Karpik.Engine.Sdk">
  <PropertyGroup>
    <KarpikProjectKind>Runtime</KarpikProjectKind>
    <KarpikSide>Client</KarpikSide>
  </PropertyGroup>
</Project>
```

Supported project kinds initially are `Runtime`, `Test`, `Tool`, `Generator`, and `Assets`. Supported sides are `Client`, `Server`, `Shared`, and `None`. A kind controls build and publish behaviour; a side controls dependency boundaries. Tests and tools use the same Karpik SDK so they cannot silently bypass analyzers or project-graph validation.

The game pins the Karpik MSBuild SDK to an exact version through the standard `msbuild-sdks` section in `global.json`. The `.slnx` remains a standard solution description and does not carry SDK configuration.

No implicit `latest`, wildcard, or nearest-compatible engine selection is allowed. A missing version is a resolution error.

### The build enforces SDK participation

When a solution is built, a validation task supplied by `Karpik.Engine.Sdk` reads the `.slnx` project list before compilation and rejects any project that does not use the Karpik SDK or omits its required kind and side. It then validates project references, module dependencies, cycles, and Client/Server/Shared boundaries.

A direct project build validates the transitive `ProjectReference` graph reachable from that project. Editor project opening performs the complete solution validation before creating an active project context. Diagnostics identify the offending project and rule instead of falling through to runtime failures.

Third-party source projects cannot be added unchanged to a game solution. They should normally be consumed as packages. A third-party project intentionally built inside the game solution must be adapted to the Karpik SDK and assigned an appropriate kind and side.

### The MSBuild SDK and engine payload are separate layers

`Karpik.Engine.Sdk` is a thin versioned MSBuild integration package. The engine SDK payload is a versioned installation containing engine assemblies, runners, built-in modules, native libraries, manifests, and the compatible editor.

The default provider resolves the exact installed payload version. A source checkout is not referenced by game projects with relative `ProjectReference` items. Instead, engine source is built and exported into a local SDK payload with the same directory contract as an installed SDK. A build-time `KarpikEngineRoot` override can select that payload without changing the game project files.

Changing the engine runtime or modules only requires rebuilding the local payload. Changing `Karpik.Engine.Sdk` itself requires publishing and selecting a development version of the MSBuild SDK package. This preserves the SDK boundary instead of making game builds depend on the engine repository layout.

SDK installation from source is transactional:

1. Build into a staging directory.
2. Validate the manifest, runners, modules, native files, and layout version.
3. Write a completion marker.
4. Atomically publish the result under a content-addressed development version.

Incomplete or damaged installations never participate in resolution.

Payload layout v3 (2026-09-16) deduplicates the installation: module
directories keep only their primary `<module-id>.dll`, every other managed
dependency ships exactly once in a top-level `shared/` directory (module
native `runtimes/` trees merge into `shared/runtimes/`), and `*.pdb` symbol
files are not staged. Editor, runners, and `native/` stay self-contained
application hosts. Build-time module references and Dynamic runtime probing
resolve `shared/`; the validator accepts layouts 2 and 3 so existing
installations keep working. Measured effect: 1845 MB -> 915 MB per install.

### Launcher and editor versions follow the engine installation

A stable Karpik launcher owns the recent-project list, installed-engine management, and editor selection. Each engine installation contains a compatible editor. On opening a game solution, the launcher reads `global.json`, resolves the exact Karpik SDK version, validates the engine-installation manifest, and starts the matching editor with the absolute `.slnx` path.

The installation manifest records at least the engine version, MSBuild SDK version, editor version, runtime protocol version, and layout version. Compatibility is decided from this manifest, not guessed from filenames.

An editor owns one active game project. Switching to a project supported by the same editor version tears down the current project and opens the new one in-process. Switching to an incompatible engine/editor version saves editor state, terminates all runtime sessions, and returns control to the launcher so it can start the correct editor.

### Runtime bundles belong to games

Runners belong to the selected engine SDK. Game assemblies and project-specific client/server bundles belong to the game build output. They must no longer be assembled under `Karpik.Editor/bin`.

The SDK build flow is:

1. Evaluate and validate the complete game project graph.
2. Compile Shared, Client, Server, and supporting projects.
3. Produce physically separated client and server game bundles.
4. Let the editor start engine-owned runners that load those bundles.

Command-line and editor builds use the same MSBuild model and normal `bin`/`obj` outputs. The editor does not maintain a second hidden build graph. Download caches and expensive derived-asset caches may live below the user's Karpik application-data directory, keyed by the solution path and build inputs.

### Active-project switching is ordered and fail-closed

Project switching performs these steps in order:

1. Reject new operations and cancel any active build.
2. Stop client sessions.
3. Stop the server session.
4. Dispose IPC endpoints, file watchers, build services, and project services.
5. Persist workspace state.
6. Destroy the old project context.
7. Resolve and validate the new SDK and solution.
8. Create the new project context and enable build/run commands.

The new project is not considered active until all resolution and validation steps succeed. A failed teardown prevents the next project from opening. A failed open leaves the editor with no active project and does not silently restart the previous game.

## Alternatives Considered

### Keep games inside the engine repository

Rejected because it preserves physical coupling, prevents independent engine-version pinning, and makes multiple games share engine build state and repository structure.

### Commit direct references to an engine checkout or submodule

Rejected as the primary model because relative `ProjectReference` paths expose the engine source layout to games and produce different build graphs for installed and source modes. A submodule may be a source provider for producing a local SDK payload, but game projects still consume the resulting SDK boundary.

### Use only NuGet package references

Rejected as the complete distribution model. NuGet works well for the thin MSBuild SDK and managed libraries, but the current engine also needs coordinated runners, native dependencies, side-specific modules, editor compatibility, and installation manifests. These remain a versioned engine payload.

### Add a `.karpik` project manifest

Deferred. The `.slnx`, `global.json`, and SDK-based `.csproj` files already contain the information required for discovery, versioning, graph evaluation, and command-line builds. A second manifest would duplicate build state. It can be introduced later only if the editor requires durable project metadata that cannot be represented by the standard .NET project model.

### Use repository-level `Directory.Build.props` as the Karpik contract

Rejected because a custom SDK expresses the build contract directly in every project, participates in standard MSBuild SDK resolution, and allows ordinary `dotnet` commands without generated bootstrap files. Standard central build files remain available to game developers for their own settings but are not required for identifying a Karpik game.

### One editor binary supports every engine version

Deferred because the current editor directly depends on engine contracts. Maintaining compatibility branches in one process would create immediate coupling across historical APIs. A stable launcher plus version-matched editors provides predictable compatibility; a version-independent shell can be reconsidered after the editor protocol stabilizes.

## Consequences

- KarpikEngine source, installed engine payloads, and game repositories become separate ownership units.
- A game is a conventional .NET solution and can build without the launcher or editor.
- Installed and source-development workflows use the same game project graph.
- Every game project is covered by Karpik analyzers and boundary validation.
- Build validation and SDK resolution allocate and perform I/O, but only during tooling and build operations; runtime hot paths are unchanged.
- Engine releases must publish a coordinated MSBuild SDK package and engine payload manifest.
- Launcher/editor compatibility and SDK layout versions become public maintenance contracts.
- Adding an unchanged third-party source project to a game solution is intentionally inconvenient; package dependencies are preferred.
- Without a custom project identifier file, moving or renaming a solution may detach user-local workspace/cache metadata. The launcher may migrate that metadata heuristically, but correctness must not depend on it.
- Source development has a two-stage flow: build/export the engine payload, then build the game. This costs more build time than direct source references but preserves reproducibility and distribution boundaries.

## Validation

- Build an external fixture game located outside the KarpikEngine repository with `dotnet restore`, `dotnet build`, `dotnet test`, and `dotnet publish` while the editor is not running.
- Build the same fixture against an installed payload and a source-built payload override.
- Verify solution validation rejects any `.csproj` that does not use `Karpik.Engine.Sdk` or lacks its kind/side declarations.
- Verify direct project builds reject invalid transitive project references.
- Cover every Client, Server, Shared, Test, Tool, Generator, and Assets dependency rule.
- Verify unknown modules, disabled dependencies, ambiguous module ids, graph cycles, and side leaks fail before compilation or runtime startup.
- Verify the launcher selects the matching editor for at least two installed engine versions and rejects incomplete or incompatible installations.
- Switch between two projects and verify no worker processes, IPC endpoints, ports, file watchers, locked files, logs, hot-reload state, or asset state leak from the previous project.
- Start one server and multiple clients and verify every runner loads only the active game's side-specific bundle.
- Confirm the editor and SDK add no code, polling, allocation, or synchronization to `Update`, `FixedUpdate`, ECS `Run`, render, or network pump paths.

## Links

- Related ADR: [[editor-desktop-stack]]
- Related ADR: [[module-graph]]
- Delivery ExecPlan: [[../../plans/versioned-sdk-external-projects-execplan]]
- Delivery board: [[../04_Roadmap/kanban-versioned-sdk-external-projects]]
- Related code: `Karpik.Editor/`, `Configurator/`, `Directory.Build.props`, `Directory.Build.targets`, `Plugins.targets`
