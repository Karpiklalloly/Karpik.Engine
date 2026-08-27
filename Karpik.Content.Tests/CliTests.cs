using System.Diagnostics;
using Xunit;

namespace Karpik.Content.Tests;

public sealed class CliTests
{
    private static string ToolProject => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Karpik.Content.Tool", "Karpik.Content.Tool.csproj"));

    private static (int ExitCode, string StdOut, string StdErr) RunTool(params string[] args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"run --project \"{ToolProject}\" -- {string.Join(' ', args.Select(a => a.Contains(' ') ? $"\"{a}\"" : a))}",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        using var proc = Process.Start(psi)!;
        string stdout = proc.StandardOutput.ReadToEnd();
        string stderr = proc.StandardError.ReadToEnd();
        proc.WaitForExit();
        return (proc.ExitCode, stdout, stderr);
    }

    [Fact]
    public void Cli_Build_Succeeds()
    {
        using var tmp = new TemporaryDirectory();
        string source = tmp.CreateSubdirectory("source");
        string output = tmp.CreateSubdirectory("output");
        TestFixtures.CreateSourceFile(source, "a.json", """{"a":1}""", logicalName: "game/a");

        var (code, stdout, stderr) = RunTool("build", "--source", source, "--output", output, "--namespace", "game");
        Assert.Equal(0, code);
        Assert.Contains("build succeeded", stdout);
        Assert.True(File.Exists(Path.Combine(output, "manifest.json")));
    }

    [Fact]
    public void Cli_Build_FailsOnInvalidSource()
    {
        using var tmp = new TemporaryDirectory();
        string source = tmp.CreateSubdirectory("source");
        string output = tmp.CreateSubdirectory("output");
        TestFixtures.CreateSourceFile(source, "a.json", """{ invalid }""", logicalName: "game/a");

        var (code, stdout, stderr) = RunTool("build", "--source", source, "--output", output, "--namespace", "game");
        Assert.Equal(2, code);
        Assert.DoesNotContain("build succeeded", stdout);
        Assert.False(File.Exists(Path.Combine(output, "manifest.json")));
    }

    [Fact]
    public void Cli_Validate_SucceedsAndNoArtifacts()
    {
        using var tmp = new TemporaryDirectory();
        string source = tmp.CreateSubdirectory("source");
        TestFixtures.CreateSourceFile(source, "a.json", """{"a":1}""", logicalName: "game/a");

        var (code, stdout, _) = RunTool("validate", "--source", source, "--namespace", "game");
        Assert.Equal(0, code);
        Assert.Contains("validate succeeded", stdout);
    }

    [Fact]
    public void Cli_Validate_FailsOnInvalid()
    {
        using var tmp = new TemporaryDirectory();
        string source = tmp.CreateSubdirectory("source");
        TestFixtures.CreateSourceFile(source, "a.json", """{ invalid }""", logicalName: "game/a");

        var (code, stdout, stderr) = RunTool("validate", "--source", source, "--namespace", "game");
        Assert.Equal(2, code);
    }

    [Fact]
    public void Cli_List_OutputsSorted()
    {
        using var tmp = new TemporaryDirectory();
        string source = tmp.CreateSubdirectory("source");
        string output = tmp.CreateSubdirectory("output");
        var idA = new Karpik.Content.Core.AssetId(Guid.Parse("22222222-2222-2222-2222-222222222222"));
        var idB = new Karpik.Content.Core.AssetId(Guid.Parse("11111111-1111-1111-1111-111111111111"));
        TestFixtures.CreateSourceFile(source, "b.json", """{"b":2}""", id: idA, logicalName: "game/b");
        TestFixtures.CreateSourceFile(source, "a.json", """{"a":1}""", id: idB, logicalName: "game/a");

        var (buildCode, _, _) = RunTool("build", "--source", source, "--output", output, "--namespace", "game");
        Assert.Equal(0, buildCode);

        string manifest = Path.Combine(output, "manifest.json");
        var (code, stdout, _) = RunTool("list", "--manifest", manifest);
        Assert.Equal(0, code);
        var lines = stdout.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(2, lines.Length);
        // First line should be smaller GUID (111...)
        Assert.StartsWith("11111111-1111-1111-1111-111111111111", lines[0]);
        Assert.StartsWith("22222222-2222-2222-2222-222222222222", lines[1]);
    }

    [Fact]
    public void Cli_Why_ShowsDependencies()
    {
        using var tmp = new TemporaryDirectory();
        string source = tmp.CreateSubdirectory("source");
        string output = tmp.CreateSubdirectory("output");
        var idA = new Karpik.Content.Core.AssetId(Guid.NewGuid());
        var idB = new Karpik.Content.Core.AssetId(Guid.NewGuid());
        TestFixtures.CreateSourceFile(source, "a.json", """{"a":1}""", id: idA, logicalName: "game/a", dependencies: new[] { idB });
        TestFixtures.CreateSourceFile(source, "b.json", """{"b":2}""", id: idB, logicalName: "game/b");

        var (buildCode, _, _) = RunTool("build", "--source", source, "--output", output, "--namespace", "game");
        Assert.Equal(0, buildCode);

        string manifest = Path.Combine(output, "manifest.json");
        var (code, stdout, _) = RunTool("why", "--manifest", manifest, idA.ToCanonicalString());
        Assert.Equal(0, code);
        Assert.Contains(idA.ToCanonicalString(), stdout);
        Assert.Contains(idB.ToCanonicalString(), stdout);
    }

    [Fact]
    public void Cli_Why_MissingId_Fails()
    {
        using var tmp = new TemporaryDirectory();
        string source = tmp.CreateSubdirectory("source");
        string output = tmp.CreateSubdirectory("output");
        TestFixtures.CreateSourceFile(source, "a.json", """{"a":1}""", logicalName: "game/a");
        var (buildCode, _, _) = RunTool("build", "--source", source, "--output", output, "--namespace", "game");
        Assert.Equal(0, buildCode);

        string manifest = Path.Combine(output, "manifest.json");
        var missing = Guid.NewGuid().ToString("D");
        var (code, stdout, stderr) = RunTool("why", "--manifest", manifest, missing);
        Assert.Equal(2, code);
    }

    [Fact]
    public void Cli_UsageError_Returns1()
    {
        var (code, _, _) = RunTool("build", "--source", "onlyOneArg");
        Assert.Equal(1, code);
    }
}
