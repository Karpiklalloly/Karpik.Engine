# KarpikEngine

**🌐 Language:** 🇺🇸 English | [🇷🇺 Русский](README.md)

> 2D-first C# game engine with ECS architecture, hot reload, and Client / Server / Shared separation

KarpikEngine is an experimental open-source engine for developing 2D games. Its priorities are data-oriented architecture, zero allocations in hot paths, predictable lifecycle behavior, and the ability to start with single-player logic without blocking a later move to multiplayer.

Latest changelog: **v0.6** — [Changelog_0.6.md](Changelog_0.6.md).

## ✨ Key Features

### 🏗️ ECS and Lifecycle
- Uses [Dragon ECS](https://github.com/DCFApixels/DragonECS) by DCFApixels
- Stores gameplay state in ECS `struct` components
- Defines a predictable engine pipeline: `Init -> Begin -> FixedUpdate -> Update -> LateUpdate -> Render -> Destroy`
- Provides `DefaultWorld`, `EventWorld`, and `MetaWorld` facades for user code
- Runs physics and gameplay simulation with fixed dt

### 🔥 Hot Reload
- Uses a restart-worker model without standard .NET Hot Reload limitations
- Preserves ECS worlds between reloads
- Recreates services, graphics resources, sockets, and process-local handles in the new worker process
- Reloads client and server independently

### 🌐 Client / Server / Shared
- Separates client, server, and shared logic at the project level
- Validates invalid dependencies through Configurator before application startup
- Includes RPC and a basic networking sample with reconnect support after Hot Reload

### 📦 Modular Architecture
- Gives modules independent lifecycle behavior and interface-based integration
- Declares dependencies through shorthand `KarpikModuleDependency` identifiers
- Validates the module graph, cycles, side leaks, and generated artifacts through Configurator
- Exposes standard `ProjectReference` items to Rider and the compiler through a generated catalog

### 🎨 2D Runtime
- Includes an OpenGL renderer, SDL2 window/input backend, and command-buffer API
- Supports rectangles, textures, atlas SDF fonts, batching, and `Camera2D`
- Includes an ImGui overlay, AssetManagement, Tween, and Lua modding
- Includes a Physics2D API, the `Physics2D.Aether2D` backend, and a platformer sample

### ⚡ Performance
- Targets zero allocations after warm-up in frame, fixed-update, render, and network hot paths
- Uses `Karpik.Jobs` internally
- Includes the ECS scheduler, no-GC value jobs, and unmanaged memory primitives added in `v0.5`

## 🚀 Quick Start

### Requirements
- [.NET 10.0 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) or higher

### Installation (engine development)
1. Clone the repository:
   ```bash
   git clone https://github.com/Karpiklalloly/Karpik.Engine.git KarpikEngine
   cd KarpikEngine
   ```

2. On Windows, publish the local SDK:
   ```powershell
   ./_scripts/Update-KarpikSdk.ps1
   ```
   The script reports the exact SDK version, publishes an immutable engine payload, and registers its local NuGet feed.

3. Create a game outside the engine repository — from the launcher or from a template using that version:
   ```powershell
   dotnet new karpik-game --name MyGame --output ../MyGame --karpik-sdk-version <sdk-version>
   cd ../MyGame
   dotnet build MyGame.slnx -m:1 -nr:false
   ```
   There are two templates: `karpik-game` (minimal game) and `karpik-coinrush` (working multiplayer CoinRush sample).

### Installation from a distribution (game development, no engine sources)
Take both directories of the required version (`<version>-sdk` and `<version>-editor-launcher`) and run in order — .NET 10 SDK required:
```powershell
sdk\setup.exe sdk --payload sdk\sdk-payload.zip
editor-launcher\setup.exe editor --payload editor-launcher\editor-payload.zip
editor-launcher\setup.exe launcher --source editor-launcher\launcher-files
```
Then start the launcher from the Start Menu and create a game from a template. Building a distribution from sources: `./_scripts/New-KarpikDistribution.ps1 -SdkVersion "0.6.0" -EngineVersion "0.6.0"`.

4. Start the Static server launcher and then the client in another terminal:
   ```powershell
   dotnet run --project Source/MyGame.Server.Launcher/MyGame.Server.Launcher.csproj --no-build
   dotnet run --project Source/MyGame.Client.Launcher/MyGame.Client.Launcher.csproj --no-build
   ```
   For the desktop workflow, run `Karpik.Launcher` from the engine checkout and open the game's `.slnx`. Launcher selects the editor matching the SDK in `global.json`. See the [game template](templates/Karpik.Game/README.md) for Static/Dynamic modes.

### Rebuild and restart
1. Stop the game sessions in the editor.
2. Change code and build the relevant launcher.
3. Restart the server and add clients in the editor. An ordinary stop/start creates fresh ECS state.

In Debug builds, enable automatic IDE debugger attachment to child processes if you want to debug the worker after restart.

> Keep state that must survive Hot Reload in ECS components. Recreate runtime resources and process-local handles.

## 🗺️ Project Status

### ✅ Implemented in v0.5
- No-GC value jobs (`IJob`/`IJobFor`, `JobScheduler`), unmanaged memory primitives (`Karpik.Memory`)
- Parallel ECS `ISystemUpdate` scheduler with static codegen and Roslyn validation
- Threaded client pipeline: off-thread simulation, triple-buffered render commands

### ✅ Implemented in v0.6
- Default static runtime composition, NativeAOT publishing of static hosts
- External versioned MSBuild SDK, `karpik-game` and `karpik-coinrush` templates
- Constructor DI (`[Export]` + `[ServiceRegistration]`, Engine/ModSet/Simulation scopes)
- Content pipeline (`AssetRef`/`Lease`, `ContentRegistry`, `ContentRefs` codegen)
- Typed network snapshot registry with protocol schema hash
- Unity-like editor, `ILogger` diagnostics, exe distribution (SDK and Launcher+Editor bundles)

### ✅ Implemented in v0.4
- ECS core, world facades, and engine-owned system lifecycle
- Client / Server / Shared boundaries and Configurator validation
- Restart-worker Hot Reload with ECS world restoration
- OpenGL 2D renderer, SDL2 window/input, batching, camera, and text
- AssetManagement, Tween, Lua modding, and Dependency Injection
- Physics2D API, Aether2D backend, and platformer sample
- Tests for lifecycle phases, ECS component lifecycle, and the module graph

### 🔮 Next Directions
- Further development of the 2D renderer, asset pipeline, input, and audio APIs
- A new UI API replacing the removed prototype UI Toolkit
- Developer tools, profiling, and networking sample improvements

See [Roadmap 1.0](docs/04_Roadmap/karpikengine-1.0-roadmap.md) for details.

## 🏗️ Project Architecture

The repository contains the engine, reusable modules, and tools. Games are created from the template in separate directories; their Client, Server, and Shared projects consume `Karpik.Engine.Sdk`.

### Main Directories
- `Modules/Client` — rendering, input, and client-side presentation
- `Modules/Server` — server logic and validation
- `Modules/Shared` — common logic independent of runtime side
- `templates/Karpik.Game` — minimal external game template with Static launchers and content
- `templates/Karpik.CoinRush` — multiplayer CoinRush sample template
- `Karpik.Editor`, `Karpik.Launcher` — desktop workspace and editor version selection
- `Karpik.Engine.Sdk`, `Karpik.Engine.Packager` — NuGet SDK and engine payload publication
- `Karpik.Engine.Setup`, `_scripts/New-KarpikDistribution.ps1` — exe installer and distribution builds (SDK and Launcher+Editor bundles)
- `Configurator` — module-graph validation and generation

### Adding a Dependency
Use `KarpikModuleDependency` in engine projects under `Modules`:

```xml
<KarpikModuleDependency Include="Physics2D" />
```

After adding, removing, or moving a project, run:

```bash
dotnet run --project Configurator/Configurator.csproj -- --generate
dotnet run --project Configurator/Configurator.csproj -- --validate
```

Adding an existing dependency identifier only requires a project reload or build.

External games select modules through `KarpikModuleSelection` in `Directory.Build.targets` and use ordinary literal `ProjectReference` items between game projects. Configurator manages the engine graph. The SDK validates the game graph and side boundaries and publishes runtime bundles into the game's own output.

## 🤝 Contributing

KarpikEngine is an open-source project. Issues and Pull Requests should respect its real-time constraints: zero allocations in hot paths, cache locality, fixed dt for simulation, and Client / Server / Shared boundaries.

## 💬 Community

- 💬 **Discord:** [https://discord.gg/UvdEuY2D2V](https://discord.gg/UvdEuY2D2V)
- 🐛 **GitHub Issues** — bugs and suggestions
- 📖 **GitHub Discussions** — general questions

## 📄 License

This project is distributed under the MIT license. See [LICENSE](LICENSE) for details.
