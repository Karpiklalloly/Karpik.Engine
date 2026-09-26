using System.Composition;
using Karpik.Engine.Core;
using Microsoft.Extensions.Logging;

namespace Karpik.Engine.Shared.Log;

[Export(typeof(ILoggerFactoryModifier))]
[ServiceRegistration(ModuleScope.Engine, ServiceLifetime.Singleton)]
public class EditorConsoleLoggerModifier : ILoggerFactoryModifier
{
    public void Modify(ILoggingBuilder builder)
    {
        if (string.Equals(
                Environment.GetEnvironmentVariable("KARPIK_EDITOR_LOG_CAPTURE"),
                "1",
                StringComparison.Ordinal))
        {
            builder.AddEditorConsole();
        }
    }
}