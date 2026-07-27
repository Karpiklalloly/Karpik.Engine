using System.Text.RegularExpressions;
using Xunit;

namespace Karpik.Engine.Packager.Tests;

public sealed class UpdateKarpikSdkScriptTests
{
    [Fact]
    public void ScriptUsesEditableSdkVersionAndPublishesAnAtomicPayload()
    {
        string repositoryRoot = Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        string scriptPath = Path.Combine(repositoryRoot, "Update-KarpikSdk.ps1");

        Assert.True(File.Exists(scriptPath), $"Missing SDK update script: {scriptPath}");
        string script = File.ReadAllText(scriptPath);

        Assert.Matches(
            new Regex(@"(?m)^\$SdkVersion\s*=\s*""[^""]+""\s*$", RegexOptions.CultureInvariant),
            script);
        Assert.Contains("Karpik.Engine.Packager", script, StringComparison.Ordinal);
        Assert.Contains("--sdk-version", script, StringComparison.Ordinal);
        Assert.Contains("ArchivedEngines", script, StringComparison.Ordinal);
        Assert.Contains("dotnet nuget", script, StringComparison.Ordinal);
        Assert.DoesNotContain("Copy-Item", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("TrimEndingDirectorySeparator", script, StringComparison.Ordinal);
    }
}
