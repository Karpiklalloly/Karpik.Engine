using Autofac;
using Karpik.Engine.Core;
using Microsoft.Extensions.Logging;

namespace Karpik.Engine.Shared.Log;

[Module(ModuleScope.Engine)]
public class LoggerModuleInstaller : IModuleInstaller
{
    public string Name => "Logger";

    public void OnRegisterServices(ContainerBuilder builder)
    {
        builder.Register(context => LoggerFactory.Create(logging =>
            {
                logging.ClearProviders();
                logging.AddSimpleConsole();
                if (string.Equals(
                        Environment.GetEnvironmentVariable("KARPIK_EDITOR_LOG_CAPTURE"),
                        "1",
                        StringComparison.Ordinal))
                {
                    logging.AddEditorConsole();
                }
                logging.SetMinimumLevel(LogLevel.Trace);

                foreach (var modifier in context.Resolve<IEnumerable<ILoggerFactoryModifier>>())
                {
                    modifier.Modify(logging);
                }
            }))
            .As<ILoggerFactory>()
            .SingleInstance();

        builder.RegisterGeneric(typeof(Microsoft.Extensions.Logging.Logger<>))
            .As(typeof(ILogger<>))
            .SingleInstance();
    }
}
