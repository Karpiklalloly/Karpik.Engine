using Karpik.Engine.Core;

namespace Karpik.Engine.Shared.Log;

[Module]
public class LoggerInstaller : IInstaller
{
    public string Name => "Logger";

    public void OnRegisterServices(IServiceRegister services, IServiceContainer serviceContainer)
    {
        services.Register<ILogger>(Logger.Instance);
    }
}