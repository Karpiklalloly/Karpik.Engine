using Karpik.Engine.Core;
using Xunit;

public sealed class ModuleStagingCleanupTests
{
    [Fact]
    public void CleanupCompletedVersions_DeletesStaleCompletedVersionAndRetainsActiveAndIncompleteVersions()
    {
        var root = Path.Combine(Path.GetTempPath(), $"KarpikEngine.Tests.{Guid.NewGuid():N}");
        var active = Path.Combine(root, "modules.version.active");
        var stale = Path.Combine(root, "modules.version.stale");
        var incomplete = Path.Combine(root, "modules.version.incomplete");

        try
        {
            Directory.CreateDirectory(active);
            File.WriteAllText(Path.Combine(active, ".complete"), "active");
            Directory.CreateDirectory(stale);
            File.WriteAllText(Path.Combine(stale, ".complete"), "stale");
            Directory.CreateDirectory(incomplete);

            ModuleStagingCleanup.CleanupCompletedVersions(root, active);

            Assert.True(Directory.Exists(active));
            Assert.False(Directory.Exists(stale));
            Assert.True(Directory.Exists(incomplete));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}
