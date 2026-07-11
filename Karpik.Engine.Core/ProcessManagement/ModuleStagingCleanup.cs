namespace Karpik.Engine.Core;

internal static class ModuleStagingCleanup
{
    public static void CleanupCompletedVersions(string baseDirectory, string activeDirectory)
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
                Console.WriteLine($"[ProcessManager] Removed stale module staging directory: {directory}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ProcessManager] Failed to remove stale module staging directory '{directory}': {ex.Message}");
            }
        }
    }
}
