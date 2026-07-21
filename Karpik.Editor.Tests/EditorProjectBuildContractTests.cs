using System.Xml.Linq;
using Xunit;

namespace Karpik.Editor.Tests;

public sealed class EditorProjectBuildContractTests
{
    [Fact]
    public void EditorProject_DoesNotPackageEditorLocalRuntimeBundles()
    {
        string repositoryRoot = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            ".."));
        string projectPath = Path.Combine(repositoryRoot, "Karpik.Editor", "Karpik.Editor.csproj");

        XDocument project = XDocument.Load(projectPath);
        string source = project.ToString(SaveOptions.DisableFormatting);

        Assert.DoesNotContain(
            project.Descendants(),
            element => element.Name.LocalName == "Import"
                       && ((string?)element.Attribute("Project"))?.Contains(
                           "EditorRuntimeBundles.targets",
                           StringComparison.OrdinalIgnoreCase) == true);
        Assert.DoesNotContain(
            project.Descendants(),
            element => element.Name.LocalName == "SkipEditorRuntimeBundles");
        Assert.DoesNotContain("EditorRuntimeBundles.targets", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SkipEditorRuntimeBundles", source, StringComparison.Ordinal);
        Assert.DoesNotContain("$(TargetDir)runtimes", source, StringComparison.Ordinal);
    }
}
