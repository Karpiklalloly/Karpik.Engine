using DCFApixels.DragonECS;
using Karpik.Engine.Core;
using KarpikGame.Shared;

[Module(ModuleScope.Simulation)]
public class ServerGameInstaller : IModuleInstaller
{
    public string Name => "KarpikGame.Server";

    public IModule CreateModule() => new ServerGameModule();
}

internal sealed class ServerGameModule : IModule
{
    public void Add(ISystemRegistry systems)
    {
        systems.Add<ServerGameInitSystem>();
    }
}

// Public: static composition emits direct factories for module systems, so every
// ECS system must be visible from the host launcher assembly.
public sealed class ServerGameInitSystem(EcsDefaultWorld world) : ISystemInit
{
    public void Init()
    {
        // Idempotent under hot-reload state restoration: the restored world
        // already contains the baseline entity, so creating another one on every
        // worker restart would grow the saved state payload linearly.
        if (world.Count == 0)
        {
            int entity = world.NewEntity();
            world.GetPool<GameComponent>().Add(entity) = new GameComponent { Value = 42 };
            Console.WriteLine($"[ServerGame] Created entity {entity} with GameComponent(42). Total entities: {world.Count}");
        }

        string contentPath = Path.Combine(Directory.GetCurrentDirectory(), "Content", "runtime.txt");
        if (File.Exists(contentPath))
        {
            Console.WriteLine($"[ServerGame] Content: {File.ReadAllText(contentPath).Trim()}");
        }
        string sharedContentPath = Path.Combine(Directory.GetCurrentDirectory(), "Content", "shared-runtime.txt");
        if (File.Exists(sharedContentPath))
        {
            Console.WriteLine($"[ServerGame] Shared content: {File.ReadAllText(sharedContentPath).Trim()}");
        }
    }
}
