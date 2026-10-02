using System.Xml.Linq;
using Xunit;

namespace StaticAnalyzer.Tests;

public sealed class CodegenDeploymentTests
{
    [Fact]
    public void DirectoryBuildProps_DeploysCoreCodegenToModuleProjects()
    {
        string repositoryRoot = FindRepositoryRoot();
        XDocument props = XDocument.Load(Path.Combine(repositoryRoot, "Directory.Build.props"));

        bool deployed = props
            .Descendants("ItemGroup")
            .Where(static group =>
                ((string?)group.Attribute("Condition"))?.Contains("MSBuildProjectDirectory", StringComparison.Ordinal) == true)
            .Elements("KarpikModuleDependency")
            .Any(static dependency =>
                string.Equals(
                    (string?)dependency.Attribute("Include"),
                    "Karpik.Engine.Core.Codegen",
                    StringComparison.Ordinal));

        Assert.True(deployed, "Karpik.Engine.Core.Codegen must run in Modules projects.");
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Directory.Build.props")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the repository root.");
    }
}
