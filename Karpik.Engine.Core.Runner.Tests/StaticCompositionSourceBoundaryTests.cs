using Xunit;

/// <summary>
/// Compile-time boundary contract for Milestone 7: the Static host surface must
/// never reference the Dynamic-only composition machinery (ModuleLoader,
/// PluginLoadContext, RuntimeModuleComposition) or reflection activation. The
/// Dynamic-only types stay in place until the default composition mode flips.
/// </summary>
public sealed class StaticCompositionSourceBoundaryTests
{
    private static readonly string[] ForbiddenTokens =
    [
        "ModuleLoader",
        "PluginLoadContext",
        "RuntimeModuleComposition",
        "Assembly.Load",
        "Assembly.GetTypes",
        "Activator.CreateInstance"
    ];

    [Fact]
    public void StaticHostSources_NeverReferenceDynamicOnlyComposition()
    {
        string sourcesRoot = Path.Combine(AppContext.BaseDirectory, "StaticCompositionSources");
        Assert.True(Directory.Exists(sourcesRoot), $"Static composition sources are missing: {sourcesRoot}");

        string[] files = Directory.GetFiles(sourcesRoot, "*.cs", SearchOption.TopDirectoryOnly);
        Assert.NotEmpty(files);

        foreach (string file in files)
        {
            string source = File.ReadAllText(file);
            foreach (string token in ForbiddenTokens)
            {
                Assert.True(
                    !source.Contains(token, StringComparison.Ordinal),
                    $"Static host source '{Path.GetFileName(file)}' must not reference '{token}'.");
            }
        }
    }

    [Fact]
    public void RunnerSource_StaticExecutionPath_NeverUsesReflectionActivation()
    {
        // Milestone 9 corrective contract: the Static startup path must receive
        // ECS registry providers through the composition contract instead of
        // enumerating assemblies reflectively. The runner source itself must be
        // free of reflection activation tokens; Dynamic-only discovery lives in
        // its own file and is never entered on the Static path.
        string runnerSource = File.ReadAllText(
            Path.Combine(FindRepositoryRoot(), "Karpik.Engine.Core.Runner", "Runner.cs"));

        string[] forbiddenTokens =
        [
            "Assembly.GetTypes",
            "Activator.CreateInstance",
            "Type.GetType"
        ];
        foreach (string token in forbiddenTokens)
        {
            Assert.True(
                !runnerSource.Contains(token, StringComparison.Ordinal),
                $"Runner.cs must not use '{token}': the Static execution path receives generated providers via IStaticRuntimeComposition.");
        }
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "Karpik.Engine.Core.Runner", "Runner.cs")))
        {
            current = current.Parent;
        }

        Assert.NotNull(current);
        return current!.FullName;
    }
}
