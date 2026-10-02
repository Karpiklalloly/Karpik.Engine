# Karpik CoinRush Sample

Games created from this template (`dotnet new karpik-coinrush`) build against the installed KarpikEngine
through the `Karpik.Engine.Sdk` MSBuild SDK. This is a working multiplayer sample: a server-authoritative
CoinRush match (Shared components, payloads and rules; Server match, physics and net systems; Client
bootstrap, input, camera, snapshot interpolation, draw and effects) with content pipeline assets
(`Content/CoinRush`, shaders, sprites, fonts) and a test suite covering rules, snapshots, payloads,
content and ECS integration. Every project declares its kind and side; composition
mode controls how the game is packaged and launched.

## Composition mode

| Mode | Default | Bundle | Host |
|---|---|---|---|
| `Static` | yes (empty/unset normalizes to Static) | manifest-free: `Content/`, `Mods/`, native assets only | your launcher executable, compiled graph |
| `Dynamic` | opt-in rollback | canonical manifest + staged module DLLs | universal `Karpik.Engine.Core.Runner.exe` |

Set the mode explicitly per project when needed:

```xml
<PropertyGroup>
  <KarpikCompositionMode>Dynamic</KarpikCompositionMode>
</PropertyGroup>
```

Accepted values are exactly `Dynamic` and `Static`; anything else fails with `KARPIK010`.

## Static build

```powershell
dotnet build src/MyGame.Server.Launcher/MyGame.Server.Launcher.csproj -m:1 -nr:false
```

The launcher output contains the whole server (or client) graph. The sibling runtime project emits a
manifest-free `karpik-bundle` with `Content/`, `Mods/`, and native dependencies.

Rules to keep a game static-host compatible:

- Keep every ECS system and cross-assembly service `public`; internal members cannot be seen by the
  generated composition.
- Do not add reflection-based module loading; static hosts never run `Assembly.GetTypes`,
  `Activator.CreateInstance`, or a `PluginLoadContext`.

## Static NativeAOT publish

Requires the Visual Studio C++ workload (linker). From a developer prompt:

```powershell
dotnet publish src/MyGame.Server.Launcher/MyGame.Server.Launcher.csproj -c Release -r win-x64 `
  -p:PublishAot=true -p:InvariantGlobalization=true -m:1 -nr:false
```

The published directory holds the self-contained native executable plus content and native
dependencies — deploy it as one folder. Use `-p:InvariantGlobalization=true` only if the game does
not need culture data.

A documented warning inventory (IL2104/IL3053 aggregate payload warnings, IL3000/IL3002 from
Silk.NET's loader probing) is suppressed by the SDK; any other trim/AOT warning is treated as an
error by the acceptance gates.

Hot reload keeps working in Static hosts: the editor/watcher restarts the process and restores ECS
state through IPC (`reload/state`), so no plugin context is required.

## Dynamic build (rollback)

Switch each runtime project to `KarpikCompositionMode=Dynamic` and build as before:

```powershell
dotnet build src/MyGame.Server.Launcher/MyGame.Server.Launcher.csproj -m:1 -nr:false
```

The runtime project stages a canonical bundle (`modules/modules.list`, versioned staging) launched
by the installed universal runner. Dynamic exists as a supported rollback path this release cycle.
