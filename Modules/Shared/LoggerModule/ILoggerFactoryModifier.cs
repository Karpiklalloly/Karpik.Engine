using Microsoft.Extensions.Logging;

namespace Karpik.Engine.Shared.Log;

public interface ILoggerFactoryModifier
{
    void Modify(ILoggingBuilder builder);
}
