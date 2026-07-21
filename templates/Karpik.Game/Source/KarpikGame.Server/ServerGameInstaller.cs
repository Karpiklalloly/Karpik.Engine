using DCFApixels.DragonECS;
using Karpik.Engine.Core;
using KarpikGame.Shared;

[Module]
public class ServerGameInstaller : IInstallerConfiguratable
{
    public string Name => "KarpikGame.Server";

    public void OnRegisterServices(IServiceRegister services, IServiceContainer container) { }

    public void OnConfigure(IServiceContainer services, IServiceRegister container, out IModule? module)
    {
        module = null;
    }

    public void OnConfigureComplete(IServiceContainer services)
    {
        var world = (EcsDefaultWorld)services.GetService(typeof(EcsDefaultWorld))!;
        int entity = world.NewEntity();
        world.GetPool<GameComponent>().Add(entity) = new GameComponent { Value = 42 };
        Console.WriteLine($"[ServerGame] Created entity {entity} with GameComponent(42). Total entities: {world.Count}");

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
