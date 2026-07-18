using DCFApixels.DragonECS;
using Karpik.Engine.Core;
using KarpikGame.Shared;

[Module]
public class ClientGameInstaller : IInstallerConfiguratable
{
    public string Name => "KarpikGame.Client";

    [DI] private EcsDefaultWorld _world = null!;

    public void OnRegisterServices(IServiceRegister services, IServiceContainer container) { }

    public void OnConfigure(IServiceContainer services, IServiceRegister container, out IModule? module)
    {
        module = null;
    }

    public void OnConfigureComplete(IServiceContainer services)
    {
        Console.WriteLine($"[ClientGame] World has {_world.Count} entities. Client initialized.");
    }
}
