# KarpikEngine: Key Features

This page describes the engine features that matter when developing and publishing a game: modular builds, the game SDK, content processing, service registration, and logging configuration. It is not a list of every gameplay subsystem.

## Modular Game Builds

The engine is composed of modules. A module can provide services, gameplay systems, and dependencies on other modules. The client, server, and shared parts of a game select their own module sets. Invalid dependencies across these boundaries are caught during the build.

Each game defines a module profile: which modules are enabled and, when a module has multiple implementations, which implementation to use. Required dependencies of selected modules are added automatically. An unknown module, missing implementation, dependency cycle, or cross-side dependency causes a build error.

### What Goes Into a Published Game

An installed engine SDK contains a catalog of all available production modules. This is a catalog to select from, not a complete game payload.

For each side, the SDK resolves the profile and uses only the selected modules and their required dependencies. Unselected implementations and unrelated modules are not included in the game package. Shared libraries required by selected modules are included as dependencies.

The profile is defined with `KarpikModuleSelection` items in the game build configuration. For example, shared gameplay and logging modules can be enabled for all sides, while graphics can be selected only for the client. A module with multiple implementations requires exactly one implementation to be selected.

Static composition is the default: the SDK generates the module and service composition when it builds the executable. In this mode, separate module files and a runtime module manifest are not needed. Dynamic composition is also supported: selected modules are included in the runtime package and loaded at startup. Dynamic builds use the same selection profile and do not load the entire SDK catalog.

In short: **unused modules are not included in the published game**. A module that was not selected explicitly is included only when it is a required dependency of a selected module.

## SDK for Game Projects

A game can live outside the engine repository and use an installed KarpikEngine version. The SDK version is pinned in the solution configuration so the build uses a compatible set of tools and modules.

Each project in the solution declares its kind and side, such as a client runtime, server runtime, shared code, test, or tool. Before compilation, the SDK validates the whole project graph:

- every project follows the SDK requirements;
- project references are explicit and form a valid graph;
- the graph has no cycles or illegal dependencies between client, server, and shared code;
- the module profile matches the available modules and their required dependencies.

This catches project-structure errors during the build instead of after the game starts. The SDK also connects the build to content processing, module selection, and separate client and server runtime packages. Builds from the editor and command line use the same project model.

Project kind and side are declared in each project:

```xml
<Project Sdk="Karpik.Engine.Sdk">
  <PropertyGroup>
    <KarpikProjectKind>Runtime</KarpikProjectKind>
    <KarpikSide>Client</KarpikSide>
  </PropertyGroup>
</Project>
```

## Content Pipeline

The Content Pipeline turns authored game assets into a validated set of data that is included in the game build. It separates source files from the files used by the runtime.

### Asset Metadata

Each source file has a companion metadata file containing:

- an immutable asset ID;
- a logical name used to find the asset in game code;
- the source type and processing settings;
- dependencies on other assets;
- the target side: client, server, or both.

The asset ID stays the same when a file is moved or renamed. The logical name can be changed independently. The pipeline validates metadata, unique IDs and names, dependencies, and whether each asset is valid for the selected build side.

### Processing and Packaging

During a build, the pipeline validates source files, processes selected assets, and produces a manifest and cooked artifacts. Output is deterministic: identical inputs produce identical output. Publication is atomic, so a failed build leaves the previous successful output available.

Client and server builds get separate asset sets according to each asset's target. The SDK automatically places the manifest and artifacts in the corresponding runtime packages.

### Supported Data and Current Limits

Currently supported sources include JSON game data, images, font descriptions, and graphics-effect source files. The pipeline can generate typed references for JSON assets from their metadata.

Image processing currently validates and packages the source data, but does not create a ready-to-use graphics object. Font descriptions and graphics-effect sources are also validated and shipped as data; runtime font creation and graphics-effect compilation are not yet part of the pipeline.

### Runtime Loading

At startup, the asset registry reads the manifest and discovers the available assets without loading every file. Game code requests an asset through a typed reference. File I/O and data conversion are performed explicitly before the asset is used in the game loop.

### Editing Asset Metadata

When a project is opened, the editor can create missing metadata for supported file types. For a selected asset, developers can change its type, logical name, target side, processing settings, and dependencies. The asset ID is read-only. Before saving, the editor validates the metadata and will not replace the file with invalid data.

## Registering and Using Services

A service is registered under a contract, usually an interface used by gameplay systems and other services. For a regular service, mark the type with two attributes:

```csharp
using System.Composition;
using Karpik.Engine.Core;

public interface IInventoryService
{
    void UpdateInventory();
}

[Export(typeof(IInventoryService))]
[ServiceRegistration(ModuleScope.Simulation, ServiceLifetime.Singleton)]
public sealed class InventoryService : IInventoryService
{
    public void UpdateInventory() { }
}

public sealed class InventorySystem(IInventoryService inventory) : ISystemUpdate
{
    public void Update()
    {
        inventory.UpdateInventory();
    }
}
```

`Export` declares the contract available to other parts of the game. `ServiceRegistration` selects the service's owning scope and lifetime. Dependencies are passed through constructors; the engine creates registered services and gameplay systems.

Scopes form a hierarchy:

- `Engine` — infrastructure for one running engine process;
- `ModSet` — services shared by simulations using the same set of mods;
- `Simulation` — services and state belonging to one game simulation.

A child scope can resolve services from its parents, while sibling simulations remain isolated. `Singleton` means one instance per owning scope, not one per process. `Transient` creates a new instance for each resolution.

Use `IModuleInstaller.OnRegisterServices` when registration needs a ready-made instance, a factory, a condition, or a type that cannot carry registration attributes. Explicit installer registrations run after attribute registrations and can intentionally override them.

## Customizing Game Logging

The engine provides a typed logger to services through constructor injection. Messages are written to the console by default; when running in the editor, they can also be captured in its log panel.

A game can change logging configuration by registering an `ILoggerFactoryModifier`. The engine calls it once when creating the logging factory, before the game simulation starts. The supplied `ILoggingBuilder` can be used to:

- add a custom message sink, such as writing to a file or forwarding to another system;
- configure filters and the minimum log level;
- clear the default sinks and replace them with custom ones.

```csharp
using System.Composition;
using Karpik.Engine.Core;
using Karpik.Engine.Shared.Log;
using Microsoft.Extensions.Logging;

[Export(typeof(ILoggerFactoryModifier))]
[ServiceRegistration(ModuleScope.Engine, ServiceLifetime.Singleton)]
public sealed class GameLoggingModifier : ILoggerFactoryModifier
{
    public void Modify(ILoggingBuilder builder)
    {
        builder.ClearProviders();
        // GameLogProvider is implemented by the game.
        builder.AddProvider(new GameLogProvider());
    }
}
```

The logging factory owns and disposes the sinks added to it. Configuration runs at startup and adds no work to the game loop.

## Restarting While Preserving State

During development, the client or server can be restarted independently. Game-world state is serialized and restored in the new process; temporary resources, connections, and process-local handles are recreated. This lets developers update code without losing transferable game state.
