# KarpikEngine

**🌐 Language:** 🇺🇸 English | [🇷🇺 Русский](README.md)

> 2D-first C# game engine with ECS architecture, hot reload, and Client / Server / Shared separation

KarpikEngine is an experimental open-source engine for developing 2D games. Its priorities are data-oriented architecture, zero allocations in hot paths, predictable lifecycle behavior, and the ability to start with single-player logic without blocking a later move to multiplayer.

Current release: **v0.4**. See [Changelog_0.4.md](Changelog_0.4.md) for details.

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
- Plans a safe scheduler for parallel user ECS systems in `v0.5`

## 🚀 Quick Start

### Requirements
- [.NET 10.0 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) or higher

### Installation and Launch
1. Clone the repository:
   ```bash
   git clone https://github.com/your-username/KarpikEngine.git
   cd KarpikEngine
   ```

2. Build the launchers:
   ```bash
   dotnet build ServerLauncher/ServerLauncher.csproj -m:1 -nr:false
   dotnet build ClientLauncher/ClientLauncher.csproj -m:1 -nr:false
   ```

3. Start the server:
   ```bash
   dotnet run --project ServerLauncher/ServerLauncher.csproj
   ```

4. Start the client in another terminal:
   ```bash
   dotnet run --project ClientLauncher/ClientLauncher.csproj
   ```

### Hot Reload
1. Change code and build the relevant launcher.
2. Click `Hot Reload` in the client debug panel or press `R` in the launcher console.
3. The new worker process restores the ECS state.

In Debug builds, enable automatic IDE debugger attachment to child processes if you want to debug the worker after restart.

> Keep state that must survive Hot Reload in ECS components. Recreate runtime resources and process-local handles.

## 🗺️ Project Status

### ✅ Implemented in v0.4
- ECS core, world facades, and engine-owned system lifecycle
- Client / Server / Shared boundaries and Configurator validation
- Restart-worker Hot Reload with ECS world restoration
- OpenGL 2D renderer, SDL2 window/input, batching, camera, and text
- AssetManagement, Tween, Lua modding, and Dependency Injection
- Physics2D API, Aether2D backend, and platformer sample
- Tests for lifecycle phases, ECS component lifecycle, and the module graph

### 🔮 Next Directions
- `v0.5`: scheduler, `Karpik.Jobs` stabilization, no-GC scheduling, and memory allocators
- Further development of the 2D renderer, asset pipeline, input, and audio APIs
- A new UI API replacing the removed prototype UI Toolkit
- Developer tools, profiling, and networking sample improvements

See [Roadmap 1.0](docs/04_Roadmap/karpikengine-1.0-roadmap.md) for details.

## 🏗️ Project Architecture

The repository is split into reusable **Modules** and **MyGame** projects. Both groups are divided into Client, Server, and Shared parts.

### Main Directories
- `Modules/Client` — rendering, input, and client-side presentation
- `Modules/Server` — server logic and validation
- `Modules/Shared` — common logic independent of runtime side
- `MyGame` — sample game with client, server, and shared projects
- `Configurator` — module-graph validation and generation

### Adding a Dependency
Use `KarpikModuleDependency`, not a direct `ProjectReference`, in projects under `Modules` and `MyGame`:

```xml
<KarpikModuleDependency Include="Physics2D" />
```

After adding, removing, or moving a project, run:

```bash
dotnet run --project Configurator/Configurator.csproj -- --generate
dotnet run --project Configurator/Configurator.csproj -- --validate
```

Adding an existing dependency identifier only requires a project reload or build.

## 🤝 Contributing

KarpikEngine is an open-source project. Issues and Pull Requests should respect its real-time constraints: zero allocations in hot paths, cache locality, fixed dt for simulation, and Client / Server / Shared boundaries.

## 💬 Community

- 💬 **Discord:** [https://discord.gg/UvdEuY2D2V](https://discord.gg/UvdEuY2D2V)
- 🐛 **GitHub Issues** — bugs and suggestions
- 📖 **GitHub Discussions** — general questions

## 📄 License

This project is distributed under the MIT license. See [LICENSE](LICENSE) for details.
