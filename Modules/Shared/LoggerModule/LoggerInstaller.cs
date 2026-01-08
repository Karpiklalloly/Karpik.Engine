using DCFApixels.DragonECS;
using Karpik.Engine.Core;

namespace Karpik.Engine.Shared.Log;

[Module]
public class LoggerInstaller : IModule
{
    public string Name => "Logger";

    public void OnRegisterServices(IServiceRegister services)
    {
        services.Register<ILogger>(Logger.Instance);
    }

    public void OnConfigure(IServiceContainer services, out IEcsModule? module)
    {
        module = null;
    }

    public void OnConfigureComplete(IServiceContainer services)
    {
        
    }
}