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
}
