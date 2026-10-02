using System.Reflection;
using Xunit;

namespace Karpik.Engine.Tooling.Tests;

public sealed class RequiredApiSurfaceTests
{
    [Fact]
    public void ToolingAssemblyDefinesMilestoneThreeTypes()
    {
        Assembly assembly = Assembly.Load("Karpik.Engine.Tooling");
        string[] expectedTypes =
        [
            "Karpik.Engine.Tooling.EngineInstallationManifest",
            "Karpik.Engine.Tooling.GlobalJsonSdkVersionReader",
            "Karpik.Engine.Tooling.EngineInstallationResolver",
            "Karpik.Engine.Tooling.EngineInstallationValidator",
            "Karpik.Engine.Tooling.AtomicDirectoryPublisher",
            "Karpik.Engine.Tooling.EditorHandoffRequest",
            "Karpik.Engine.Tooling.EditorExitCodes"
        ];

        foreach (string expectedType in expectedTypes)
        {
            Assert.NotNull(assembly.GetType(expectedType));
        }
    }
}
