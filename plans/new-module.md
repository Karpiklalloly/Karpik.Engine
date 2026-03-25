# New Module

**Purpose**: Create a new module for KarpikEngine

## Context

KarpikEngine uses a modular architecture. Each module is an independent unit registered via an Installer class. Modules can be in any combination of Shared/Client/Server — not all required.

## Input

- **Module name**: e.g., Physics2D, Network, UI
- **Purpose**: What functionality does it provide?
- **Sides needed**: Which of Client/Server/Shared? (any combination)

## Structure

Each module typically consists of:
1. **Core** — core logic, interfaces, components (shared between implementations)
2. **Implementation** — specific implementation (Aether2D, LiteNetLib, etc.)

```
Modules/Shared/ModuleName/
├── ModuleName.Core/           # Core logic, interfaces
│   ├── Components.cs
│   ├── ModuleNameInstaller.cs # [Module] class implementing IModule
│   └── ECS/                   # ECS systems
└── ModuleName.Implementation/ # Specific implementation
    ├── ImplementationInstaller.cs
    └── ...
```

## Output

1. **Core module installer** (`ModuleNameInstaller.cs`):
   ```csharp
   [Module]
   public class ModuleNameInstaller : IModule, IModuleConfiguratable
   {
       public string Name => "ModuleName";
       
       public void OnRegisterServices(IServiceRegister services)
       {
           // Register services
       }
       
       public void OnConfigure(IServiceContainer services, IServiceRegister container)
       {
           // Register ECS modules, systems, etc.
           container.Register<IEcsModule>(new ModuleNameEcsModule());
       }
       
       public void OnConfigureComplete(IServiceContainer services) { }
   }
   ```

2. **ECS Module** (if needed):
   ```csharp
   internal class ModuleNameModule : IEcsModule
   {
       public void Import(EcsPipeline.Builder b)
       {
           b.Add(new MySystem());
       }
   }
   ```

3. **Implementation installer** (if multiple implementations):
   ```csharp
   public class ModuleNameImplementationInstaller : IModuleConfiguratable
   {
       public string Name => "ModuleName.Implementation";
       
       public void OnRegisterServices(IServiceRegister services) { }
       
       public void OnConfigure(IServiceContainer services, IServiceRegister container)
       {
           // Register specific implementation
       }
       
       public void OnConfigureComplete(IServiceContainer services) { }
   }
   ```

## Constraints

- **DO** name the main installer: `ModuleNameInstaller`
- **DO** implement `IModule` (optionally `IModuleConfiguratable`)
- **DO** use `[Module]` attribute on the installer class
- **DO NOT** import Server module in Client and vice versa
- **DO** keep module independent — use interfaces for dependencies
- **DO** place in appropriate Side folder (Shared/Client/Server)
- Modules are optional — can be Shared-only, Client-only, Server-only, or any combination