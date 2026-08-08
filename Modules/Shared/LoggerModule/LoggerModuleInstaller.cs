using Autofac;
using Karpik.Engine.Core;
using Microsoft.Extensions.DependencyInjection;

namespace Karpik.Engine.Shared.Log;

[Module]
public class LoggerModuleInstaller : IModuleInstaller
{
    public string Name => "Logger";

    public void OnRegisterServices(ContainerBuilder builder)
    {
        builder.Register<ILoggerKarpik>(Logger.Instance);
    }
}