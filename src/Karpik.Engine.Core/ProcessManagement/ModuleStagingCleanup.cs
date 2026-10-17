using Microsoft.Extensions.Logging;

namespace Karpik.Engine.Core;

internal sealed class ModuleStagingCleanup
{
    private readonly ILogger<ModuleStagingCleanup> _logger;

    public ModuleStagingCleanup(ILoggerFactory loggerFactory)
    {
        _logger = loggerFactory.CreateLogger<ModuleStagingCleanup>();
    }

    public static void CleanupCompletedVersions(string baseDirectory, string activeDirectory)
    {
        using ILoggerFactory loggerFactory = HostLogging.CreateDefaultFactory();
        new ModuleStagingCleanup(loggerFactory).Cleanup(baseDirectory, activeDirectory);
    }

    public void Cleanup(string baseDirectory, string activeDirectory)
    {
        if (!Directory.Exists(baseDirectory))
        {
            return;
        }

        string activePath = Path.GetFullPath(activeDirectory);
        foreach (string directory in Directory.GetDirectories(baseDirectory, "modules.version.*", SearchOption.TopDirectoryOnly))
        {
            if (string.Equals(Path.GetFullPath(directory), activePath, StringComparison.OrdinalIgnoreCase)
                || !File.Exists(Path.Combine(directory, ".complete")))
            {
                continue;
            }

            try
            {
                Directory.Delete(directory, recursive: true);
                _logger.LogInformation("Removed stale module staging directory {Directory}", directory);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to remove stale module staging directory {Directory}", directory);
            }
        }
    }
}
