# KarpikEngine

**🌐 Language:** 🇺🇸 English | [🇷🇺 Русский](README.md)

> Game engine for indie developers with ECS architecture and built-in multiplayer support

KarpikEngine is a modern alternative to MonoGame, created specifically for developing multiplayer games. The engine is built on Entity-Component-System architecture principles and ensures clear separation of client and server logic from day one of development.

## ✨ Key Features

### 🏗️ Dragon ECS Architecture
- Based on [Dragon ECS](https://github.com/DCFApixels/DragonECS) by DCFApixels
- High-performance entity and component processing
- Intuitive system for developers

### 🌐 Mandatory Client-Server Separation
- Even single-player games are designed with client-server separation
- Easy adaptation to multiplayer mode at any point in development
- Built-in RPC system for communication

### 🔧 Lua Modding Support
- Integration with MoonSharp for executing Lua scripts
- Automatic code generation planned to simplify mod creation
- Ability to add new functionality through mods

## ⚠️ Current Project Status

**KarpikEngine is in early development** and suitable for:
- 📱 **2D games** (3D support planned for the future)
- 🧪 **Experimental projects** and ECS architecture learning
- 👨‍💻 **Developers with basic ECS knowledge** and RPC programming experience

**Not recommended for:**
- 🚫 Gamedev beginners (few ready-made features yet)
- 🚫 Commercial projects with tight deadlines
- 🚫 3D games (support planned)

> 💡 **Who is this engine for?** KarpikEngine has an excellent architectural foundation, but requires developers to be ready to work with an evolving tool and contribute to its development.

## 🚀 Quick Start

### Requirements
- [.NET 9.0 SDK](https://dotnet.microsoft.com/download/dotnet/9.0) or higher

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

> ⚠️ **Note:** There are currently no ready-made demo games or tutorials. To learn the engine, it's recommended to study the source code and existing systems.

## 🗺️ Development Plans

### 🎯 Next Version
**Main goals:**
- ✅ **Full client-server architecture readiness** - completion of RPC system and network interaction
- 🎨 **Rendering API** - basic capabilities for 2D rendering
- 🔊 **Audio API** - audio playback system
- ⌨️ **Input API** - keyboard, mouse, and gamepad handling

### 🔮 Long-term Plans
- **3D support** - after verifying 2D functionality stability
- **Extended modding** - full Lua integration with code generation
- **Developer tools** - editors, debuggers, profilers
- **Documentation and examples** - tutorials and demo projects

> 📝 **Roadmap:** There's no detailed roadmap with dates yet. Development happens as features are ready and based on community feedback.

## 🤝 Contributing

KarpikEngine is an open-source project, and we welcome any contribution to its development!

### How to help the project:
- 💡 **Suggest new features** - create Issues with ideas and suggestions
- 🔧 **Make Pull Requests** - fix bugs, add features, improve code
- 📝 **Improve documentation** - help other developers understand the engine
- 🐛 **Report bugs** - test the engine and share found issues
- 💬 **Participate in discussions** - share experience and help others

### Contribution process:
1. Fork the repository
2. Create a branch for your changes
3. Make changes and test them
4. Create a Pull Request with description of changes

## 💬 Community

- 💬 **Discord:** [https://discord.gg/UvdEuY2D2V](https://discord.gg/UvdEuY2D2V) - main place for developer communication
- 🐛 **GitHub Issues** - for bugs and feature requests
- 📖 **GitHub Discussions** - for general questions and discussions

## 📄 License

This project is distributed under the MIT license. Details in the [LICENSE](LICENSE) file.

---

**KarpikEngine** - create multiplayer games with modern architecture! 🎮✨