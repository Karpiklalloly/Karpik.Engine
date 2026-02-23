# v0.3

## Key Changes
* **Assets:** Added a system for loading and unloading resources.
* **Main Thread Execution:** Added MainThreadScheduler to execute logic in the main thread, for example, for working with graphics.
* **Project Architecture:** The project is divided into modules and game elements. There is no fundamental difference, except that MyGame.**Side**.Main implies using all available modules and subprojects of MyGame. While Modules reference only the other modules they need.
* **Karpik.Jobs:** Implemented throughout the project.
* **Hot Reload:** Added support for hot reload. Full support. Without limitations of standard .NET.

## Assets
* Added AssetManagement module. Used for unified loading and unloading of assets (textures, configs).
* Usage can be seen in SpriteRenderer and DemoModuleClient.

## Main Thread Execution
* Added MainThreadScheduler class. Used for executing actions in the main thread.

## Project Architecture
* The project is divided into Modules and Game. Each of them is split into Client, Server, and Shared.
* Modules imply generalized logic, not dependent on a specific game. For example, AssetManagement, which is used in every game.
* Modules can react to different events, see IModule.cs in Karpik.Engine.Core.Runner project for details. ModuleAttribute and IModule are mandatory.
* For Game projects, the behavior is similar.
* If you add a new Module or Game project, run the command `dotnet run --project Configurator/Configurator.csproj -- --generate`. It will automatically generate files based on all projects.

## Hot Reload
* Oh yes, this took the most time.
* Currently, a separate process is used for Hot Reload, into which all libraries are loaded.
* For correct operation in Debug mode, enable automatic debugger attachment to child processes in your IDE settings.
* How it works: You start the game, the game logic doesn't work correctly, you change the logic, you trigger Build project, **check if files in the modules directory were updated (usually you need to trigger Build 2 times)**, click on `Hot Reload` in the debug panel.
* By default, all entities in each ECS world are preserved, so it's recommended to store all data in ECS worlds. Also, you can configure your own saving variation.
