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
        string normalizedSource = Normalize(project.ToString(SaveOptions.DisableFormatting));

        Assert.DoesNotContain(
            project.Descendants(),
            element => element.Name.LocalName == "Import"
                       && Normalize((string?)element.Attribute("Project") ?? "").Contains(
                           "editorruntimebundles.targets",
                           StringComparison.Ordinal));
        Assert.DoesNotContain(
            project.Descendants(),
            element => element.Name.LocalName.Equals(
                "SkipEditorRuntimeBundles",
                StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain("editorruntimebundles.targets", normalizedSource, StringComparison.Ordinal);
        Assert.DoesNotContain("skipeditorruntimebundles", normalizedSource, StringComparison.Ordinal);
        Assert.False(ContainsEditorLocalRuntimeOutput(normalizedSource));
    }

    [Theory]
    [InlineData("$(TargetDir)runtimes")]
    [InlineData("$(TargetDir)\\runtimes")]
    [InlineData("$(TARGETDIR)/RUNTIMES")]
    public void EditorLocalRuntimeOutputDetection_NormalizesCaseAndSlashes(string value) =>
        Assert.True(ContainsEditorLocalRuntimeOutput(Normalize(value)));

    private static string Normalize(string value) =>
        value.Replace('\\', '/').ToLowerInvariant();

    private static bool ContainsEditorLocalRuntimeOutput(string normalizedValue) =>
        normalizedValue.Contains("$(targetdir)runtimes", StringComparison.Ordinal)
        || normalizedValue.Contains("$(targetdir)/runtimes", StringComparison.Ordinal);
}
