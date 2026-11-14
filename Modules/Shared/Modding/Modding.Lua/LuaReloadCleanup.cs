using Karpik.Engine.Core;
using Microsoft.Extensions.Logging;
using MoonSharp.Interpreter;

namespace Karpik.Engine.Shared.Modding.Lua;

/// <summary>Maintenance-only best-effort teardown; VM callback failures never hide host cleanup failures.</summary>
internal static class LuaReloadCleanup
{
    internal static void DisposeContainers(IReadOnlyList<ModContainer> containers, ILogger logger)
    {
        List<Exception>? failures = null;
        for (int index = containers.Count - 1; index >= 0; index--)
        {
            try { containers[index].DisposeForCodeReload(); }
            catch (Exception error) { Collect(error, logger, ref failures); }
        }
        if (failures is not null)
            throw new SimulationCodeReloadCleanupException("Lua revision host cleanup failed.", new AggregateException(failures));
    }

    private static void Collect(Exception error, ILogger logger, ref List<Exception>? failures)
    {
        if (error is AggregateException aggregate)
        {
            foreach (Exception inner in aggregate.InnerExceptions) Collect(inner, logger, ref failures);
        }
        else if (error is InterpreterException)
        {
            try { logger.LogError(error, "Lua revision cleanup callback failed; remaining cleanup continues"); }
            catch (Exception loggingError)
            {
                // Logging is a host operation. Its failure is fatal, but must not prevent
                // cleanup attempts for the other runtime owners in this revision.
                (failures ??= []).Add(loggingError);
            }
        }
        else
            (failures ??= []).Add(error);
    }
}
