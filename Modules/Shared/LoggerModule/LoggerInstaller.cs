using DCFApixels.DragonECS;
using Karpik.Engine.Core;

namespace Karpik.Engine.Shared.Log;

[Module]
public class LoggerInstaller : IModule, IModuleConfiguratable
{
    public string Name => "Logger";

    public void OnRegisterServices(IServiceRegister services)
    {
        services.Register<ILogger>(Logger.Instance);
    }

    public void OnConfigure(IServiceContainer services, IServiceRegister container)
    {
    }

    public void OnConfigureComplete(IServiceContainer services)
    {
        
    }
}