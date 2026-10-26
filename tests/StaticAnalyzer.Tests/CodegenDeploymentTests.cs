using System.Xml.Linq;
using Xunit;

namespace StaticAnalyzer.Tests;

public sealed class CodegenDeploymentTests
{
    [Fact]
    public void ModuleCatalog_DeploysCoreCodegenToDependentProjects()
    {
        string repositoryRoot = FindRepositoryRoot();
        XDocument props = XDocument.Load(Path.Combine(repositoryRoot, "Generated", "KarpikModuleCatalog.props"));

        bool deployed = props
            .Descendants("ItemGroup")
            .Where(static group => group.Attribute("Condition") != null)
            .Elements("ProjectReference")
            .Any(static dependency =>
                ((string?)dependency.Attribute("Include"))?.EndsWith("Karpik.Engine.Core.Codegen.csproj", StringComparison.Ordinal) == true
                && (string?)dependency.Attribute("OutputItemType") == "Analyzer"
                && (string?)dependency.Attribute("ReferenceOutputAssembly") == "false");

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
