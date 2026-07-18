using DCFApixels.DragonECS;
using Karpik.Engine.Core;
using KarpikGame.Shared;

[Module]
public class ServerGameInstaller : IInstallerConfiguratable
{
    public string Name => "KarpikGame.Server";

    [DI] private EcsDefaultWorld _world = null!;

    public void OnRegisterServices(IServiceRegister services, IServiceContainer container) { }

    public void OnConfigure(IServiceContainer services, IServiceRegister container, out IModule? module)
    {
        module = null;
    }

    public void OnConfigureComplete(IServiceContainer services)
    {
        int entity = _world.NewEntity();
        _world.GetPool<GameComponent>().Add(entity) = new GameComponent { Value = 42 };
        Console.WriteLine($"[ServerGame] Created entity {entity} with GameComponent(42). Total entities: {_world.Count}");
    }
}
