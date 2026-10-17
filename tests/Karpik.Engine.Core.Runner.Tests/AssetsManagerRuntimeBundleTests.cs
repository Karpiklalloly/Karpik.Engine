using Karpik.Engine.Shared.AssetManagement.Core;
using Karpik.Engine.Core.FileSystem;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

[Collection(nameof(CurrentDirectoryCollection))]
public sealed class AssetsManagerRuntimeBundleTests
{
    [Fact]
    public void Paths_AreRootedAtRuntimeBundleWorkingDirectory()
    {
        string originalWorkingDirectory = Environment.CurrentDirectory;
        string bundleRoot = Path.Combine(
            Path.GetTempPath(),
            "karpik-assets-root-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(bundleRoot);
        try
        {
            Environment.CurrentDirectory = bundleRoot;
            var fileSystem = new PhysicalFileSystem();

            Assert.Equal(bundleRoot, fileSystem.RootPath);
            Assert.Equal(Path.Combine(bundleRoot, "Content"), fileSystem.ContentPath);
        }
        finally
        {
            Environment.CurrentDirectory = originalWorkingDirectory;
            Directory.Delete(bundleRoot, recursive: true);
        }
    }
}

[CollectionDefinition(nameof(CurrentDirectoryCollection), DisableParallelization = true)]
public sealed class CurrentDirectoryCollection;
