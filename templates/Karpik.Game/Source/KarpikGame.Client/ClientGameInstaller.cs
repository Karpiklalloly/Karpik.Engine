using DCFApixels.DragonECS;
using Karpik.Engine.Core;
using KarpikGame.Shared;

[Module]
public class ClientGameInstaller : IInstallerConfiguratable
{
    public string Name => "KarpikGame.Client";

    public void OnRegisterServices(IServiceRegister services, IServiceContainer container) { }

    public void OnConfigure(IServiceContainer services, IServiceRegister container, out IModule? module)
    {
        module = null;
    }

    public void OnConfigureComplete(IServiceContainer services)
    {
        var world = (EcsDefaultWorld)services.GetService(typeof(EcsDefaultWorld))!;
        Console.WriteLine($"[ClientGame] World has {world.Count} entities. Client initialized.");

        string contentPath = Path.Combine(Directory.GetCurrentDirectory(), "Content", "runtime.txt");
        if (File.Exists(contentPath))
        {
            Console.WriteLine($"[ClientGame] Content: {File.ReadAllText(contentPath).Trim()}");
        }
    }
}
