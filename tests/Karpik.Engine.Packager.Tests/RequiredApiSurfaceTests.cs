using System.Reflection;
using System.IO;
using Xunit;

namespace Karpik.Engine.Packager.Tests;

public sealed class RequiredApiSurfaceTests
{
    [Fact]
    public void PackagerAssemblyDefinesMilestoneThreeTypes()
    {
        Assembly assembly = Assembly.Load("Karpik.Engine.Packager");

        Assert.NotNull(assembly.GetType("Karpik.Engine.Packager.EnginePayloadBuilder"));
        Assert.NotNull(assembly.GetType("Karpik.Engine.Packager.PayloadLayout"));
        Type? program = assembly.GetType("Karpik.Engine.Packager.Program");
        Assert.NotNull(program);
        MethodInfo? run = program.GetMethod("Run", BindingFlags.Public | BindingFlags.Static);
        Assert.NotNull(run);
        using var output = new StringWriter();
        using var error = new StringWriter();

        int exitCode = Assert.IsType<int>(run.Invoke(null, [Array.Empty<string>(), output, error]));

        Assert.Equal(2, exitCode);
        Assert.Contains("--source", error.ToString(), StringComparison.Ordinal);
    }
}
