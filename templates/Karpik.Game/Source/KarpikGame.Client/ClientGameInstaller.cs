using DCFApixels.DragonECS;
using Karpik.Engine.Core;
using KarpikGame.Shared;

[Module(ModuleScope.Simulation)]
public class ClientGameInstaller : IModuleInstaller
{
    public string Name => "KarpikGame.Client";

    public IModule CreateModule() => new ClientGameModule();
}

internal sealed class ClientGameModule : IModule
{
    public void Add(ISystemRegistry systems)
    {
        systems.Add<ClientGameInitSystem>();
    }
}

// Public: static composition emits direct factories for module systems, so every
// ECS system must be visible from the host launcher assembly.
public sealed class ClientGameInitSystem(EcsDefaultWorld world) : ISystemInit
{
    public void Init()
    {
        Console.WriteLine($"[ClientGame] World has {world.Count} entities. Client initialized.");

        string contentPath = Path.Combine(Directory.GetCurrentDirectory(), "Content", "runtime.txt");
        if (File.Exists(contentPath))
        {
            Console.WriteLine($"[ClientGame] Content: {File.ReadAllText(contentPath).Trim()}");
        }
    }
}
