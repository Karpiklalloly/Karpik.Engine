using System.Text.RegularExpressions;
using Xunit;

namespace Karpik.Engine.Sdk.IntegrationTests;

/// <summary>
/// Static composition emits direct factories for every ECS system into the game
/// host launcher assembly, so module systems must be visible from that assembly.
/// Internal systems silently drop out of the generated composition (Dynamic mode
/// activates them through in-assembly Autofac registration) and crash static
/// hosts at startup with "service has not been registered".
/// </summary>
public sealed class EcsSystemVisibilitySourceTests
{
    private static readonly Regex InternalSystemPattern = new(
        @"^\s*internal\s+(?:sealed\s+|static\s+)*class\s+\w+[^\r\n:]*:\s*[^\r\n]*ISystem",
        RegexOptions.Compiled | RegexOptions.Multiline | RegexOptions.CultureInvariant);

    [Fact]
    public void ModuleEcsSystems_AreVisibleFromHostLauncherAssemblies()
    {
        string modulesRoot = Path.Combine(GetRepositoryRoot(), "Modules");
        Assert.True(Directory.Exists(modulesRoot), $"Modules directory is missing: {modulesRoot}");

        List<string> violations = [];
        foreach (string sourceFile in Directory.EnumerateFiles(modulesRoot, "*.cs", SearchOption.AllDirectories))
        {
            if (Path.GetFileName(sourceFile).EndsWith(".Generated.cs", StringComparison.Ordinal))
            {
                continue;
            }

            string source = File.ReadAllText(sourceFile);
            foreach (Match match in InternalSystemPattern.Matches(source))
            {
                violations.Add($"{sourceFile}: {match.Value.Trim()}");
            }
        }

        Assert.True(violations.Count == 0,
            "Module ECS systems must not be internal (invisible to generated static composition):" +
            Environment.NewLine + string.Join(Environment.NewLine, violations));
    }

    private static string GetRepositoryRoot() =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
}
