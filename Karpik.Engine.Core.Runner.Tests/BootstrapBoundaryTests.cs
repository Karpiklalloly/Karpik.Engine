using Karpik.Engine.Core;
using Xunit;

[Collection(nameof(WorkingDirectoryCollection))]
public sealed class BootstrapBoundaryTests
{
    [Fact]
    public void ExplicitRunner_DoesNotResolveRunnerAssemblyFromWorkingDirectory()
    {
        string previousDirectory = Directory.GetCurrentDirectory();
        string isolatedDirectory = Path.Combine(Path.GetTempPath(), $"karpik-bootstrap-{Guid.NewGuid():N}");
        Directory.CreateDirectory(isolatedDirectory);

        try
        {
            Directory.SetCurrentDirectory(isolatedDirectory);

            _ = new Bootstrap(Side.Server, new EngineRunner());
        }
        finally
        {
            Directory.SetCurrentDirectory(previousDirectory);
            Directory.Delete(isolatedDirectory, recursive: true);
        }
    }
}

[CollectionDefinition(nameof(WorkingDirectoryCollection), DisableParallelization = true)]
public sealed class WorkingDirectoryCollection;
