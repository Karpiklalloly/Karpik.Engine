# KarpikEngine

**🌐 Language:** 🇺🇸 English | [🇷🇺 Русский](README.md)

> Game engine for indie developers with ECS architecture, hot reload, and built-in multiplayer support

KarpikEngine is a modern game engine designed for developing multiplayer games. Built on Entity-Component-System architecture, it ensures clear separation of client and server logic from day one.

## ✨ Key Features

### 🏗️ Dragon ECS Architecture
- Based on [Dragon ECS](https://github.com/DCFApixels/DragonECS) by DCFApixels
- High-performance entity and component processing
- Intuitive system for developers

### 🔥 Hot Reload
- Full Hot Reload support without standard .NET limitations
- Change code and see results immediately without restarting the game
- All ECS world entities are preserved between reloads

### 🌐 Client-Server Architecture
- Even single-player games are designed with client-server separation
- Easy adaptation to multiplayer mode at any point
- Built-in RPC system for communication

### 📦 Modular Architecture
- Clear separation into Client, Server, and Shared parts
- Reusable modules across projects
- Automatic configuration generation via Configurator

### ⚡ Job System
- Multi-threaded data processing via Karpik.Jobs
- Parallel execution of ECS systems
- Efficient utilization of multi-core processors

### 🎨 UI System
- HTML+CSS inspired interface system
- Flexible layout and styling
- Integration with ECS architecture

### 🔧 Lua Modding Support
- Integration with MoonSharp for executing Lua scripts
- Ability to add new functionality through mods

## 🚀 Quick Start

### Requirements
- [.NET 10.0 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) or higher

### Installation
1. Clone the repository:
   ```bash
   git clone https://github.com/your-username/KarpikEngine.git
   cd KarpikEngine
   ```

2. Build the project:
   ```bash
   dotnet build
   ```

3. Run the server:
   ```bash
   dotnet run --project ServerLauncher
   ```

4. In another terminal, run the client:
   ```bash
   dotnet run --project ClientLauncher
   ```

### Hot Reload
For Hot Reload to work in Debug mode:
1. Enable automatic debugger attachment to child processes in your IDE settings
2. Start the game
3. Change code and build the project (may require 2 builds)
4. Click `Hot Reload` in the debug panel

> 💡 **Tip:** It's recommended to store all data in ECS worlds — they are automatically preserved during hot reload.

## 🗺️ Current Capabilities

### ✅ Implemented
- **ECS Core** — full Dragon ECS integration
- **Client-Server Architecture** — RPC system and networking
- **Hot Reload** — full hot reload support
- **Asset Management** — resource loading and unloading
- **Job System** — multi-threaded processing
- **UI System** — HTML+CSS style interfaces
- **Tween System** — animations and transitions (GTweens)
- **Dependency Injection** — AutoInject for systems
- **2D Rendering** — basic rendering capabilities

### 🔮 In Development
- **Audio API** — audio playback system
- **Input API** — keyboard, mouse, and gamepad handling
- **3D Support** — after 2D functionality stabilization
- **Extended Modding** — full Lua integration with code generation
- **Developer Tools** — editors, debuggers, profilers
- **A lot of other features**

## 🏗️ Project Architecture

The project is divided into **Modules** and **Game Projects**. Each of them is split into Client, Server, and Shared parts.

### Modules
- Contain generalized logic independent of a specific game
- Examples: AssetManagement, Network, UI
- Can react to lifecycle events (see [`IModule.cs`](Karpik.Engine.Core.Runner/IModule.cs))

### Game Projects
- MyGame.**Client**.Main — client part
- MyGame.**Server**.Main — server part
- MyGame.**Shared**.Main — shared logic

### Adding a New Module
After creating a new project, run:
```bash
dotnet run --project Configurator/Configurator.csproj -- --generate
```

## 🤝 Contributing

KarpikEngine is an open-source project, and we welcome any contribution!

### How to help the project:
- 💡 **Suggest new features** — create Issues with ideas
- 🔧 **Make Pull Requests** — fix bugs, add features
- 📝 **Improve documentation** — help other developers
- 🐛 **Report bugs** — test and share issues

## 💬 Community

- 💬 **Discord:** [https://discord.gg/UvdEuY2D2V](https://discord.gg/UvdEuY2D2V)
- 🐛 **GitHub Issues** — for bugs and suggestions
- 📖 **GitHub Discussions** — for general questions

## 📄 License

This project is distributed under the MIT license. Details in the [LICENSE](LICENSE) file.
