using System.Diagnostics;
using System.Text.Json;
using Karpik.Content.Core;
using Xunit;

namespace Karpik.Content.Tests;

public sealed class CliTests
{
    private static string ToolDll
    {
        get
        {
            // Prefer built content.dll next to test assembly (via ProjectReference) or fallback to Tool bin
            string fromTestBin = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "content.dll"));
            if (File.Exists(fromTestBin)) return fromTestBin;
            string fromToolBin = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Karpik.Content.Tool", "bin", "Debug", "net10.0", "content.dll"));
            if (File.Exists(fromToolBin)) return fromToolBin;
            // Fallback to Tool project path (will trigger build, but we avoid it)
            return fromToolBin;
        }
    }

    private static (int ExitCode, string StdOut, string StdErr) RunTool(params string[] args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"\"{ToolDll}\" {string.Join(' ', args.Select(a => a.Contains(' ') ? $"\"{a}\"" : a))}",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        using var proc = Process.Start(psi)!;
        // Async read both streams to avoid deadlock (VSTest hang after 10s)
        var stdoutTask = proc.StandardOutput.ReadToEndAsync();
        var stderrTask = proc.StandardError.ReadToEndAsync();
        bool exited = proc.WaitForExit(10000);
        if (!exited)
        {
            try { proc.Kill(entireProcessTree: true); } catch { }
            // Ensure we still collect output
            Task.WaitAll(new Task[] { stdoutTask, stderrTask }, TimeSpan.FromSeconds(2));
            string so = stdoutTask.IsCompleted ? stdoutTask.Result : "";
            string se = stderrTask.IsCompleted ? stderrTask.Result : "";
            throw new TimeoutException($"content tool hang (args: {string.Join(' ', args)}). stdout: {so} stderr: {se}");
        }
        // Ensure async reads completed (with timeout guard)
        Task.WaitAll(new Task[] { stdoutTask, stderrTask }, TimeSpan.FromSeconds(5));
        string stdout = stdoutTask.IsCompleted ? stdoutTask.Result : "";
        string stderr = stderrTask.IsCompleted ? stderrTask.Result : "";
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
    public void Cli_Create_WritesMetaForJsonSource()
    {
        using var tmp = new TemporaryDirectory();
        string source = tmp.CreateSubdirectory("source");
        string relativeFile = Path.Combine("config", "player.json");
        string sourceFile = Path.Combine(source, relativeFile);
        Directory.CreateDirectory(Path.GetDirectoryName(sourceFile)!);
        File.WriteAllText(sourceFile, """{"name":"Player"}""");

        var (code, _, _) = RunTool("create", "--source", source, "--file", relativeFile, "--namespace", "game");

        string metaPath = sourceFile + ".meta";
        Assert.Equal(0, code);
        Assert.True(File.Exists(metaPath));
        using JsonDocument meta = JsonDocument.Parse(File.ReadAllText(metaPath));
        Assert.Equal(1, meta.RootElement.GetProperty("schemaVersion").GetInt32());
        Assert.Equal("raw-json", meta.RootElement.GetProperty("declaredType").GetString());
        Assert.Equal("game/config/player", meta.RootElement.GetProperty("logicalName").GetString());
        Assert.True(AssetId.TryParse(meta.RootElement.GetProperty("assetId").GetString(), out _));
    }

    [Theory]
    [InlineData("Sprites/player.png", "texture", "game/Sprites/player")]
    [InlineData("Sprites/player.jpg", "texture", "game/Sprites/player")]
    [InlineData("Sprites/player.jpeg", "texture", "game/Sprites/player")]
    [InlineData("Fonts/default.font-json", "font-json", "game/Fonts/default.font")]
    [InlineData("Shaders/main.vert", "shader", "game/Shaders/main.vert")]
    [InlineData("Shaders/main.frag", "shader", "game/Shaders/main.frag")]
    public void Cli_Create_WritesMetaForSupportedSource(
        string relativeFile,
        string declaredType,
        string logicalName)
    {
        using var tmp = new TemporaryDirectory();
        string source = tmp.CreateSubdirectory("source");
        string sourceFile = Path.Combine(source, relativeFile);
        Directory.CreateDirectory(Path.GetDirectoryName(sourceFile)!);
        File.WriteAllBytes(sourceFile, [1]);

        var (code, _, _) = RunTool("create", "--source", source, "--file", relativeFile, "--namespace", "game");

        Assert.Equal(0, code);
        using JsonDocument meta = JsonDocument.Parse(File.ReadAllText(sourceFile + ".meta"));
        Assert.Equal(declaredType, meta.RootElement.GetProperty("declaredType").GetString());
        Assert.Equal(logicalName, meta.RootElement.GetProperty("logicalName").GetString());
    }

    [Fact]
    public void Cli_Create_RefusesToOverwriteExistingMeta()
    {
        using var tmp = new TemporaryDirectory();
        string source = tmp.CreateSubdirectory("source");
        string sourceFile = Path.Combine(source, "player.json");
        File.WriteAllText(sourceFile, "{}");
        string metaPath = sourceFile + ".meta";
        File.WriteAllText(metaPath, "preserve");

        var (code, _, _) = RunTool("create", "--source", source, "--file", "player.json", "--namespace", "game");

        Assert.Equal(2, code);
        Assert.Equal("preserve", File.ReadAllText(metaPath));
    }

    [Fact]
    public void Cli_Create_RejectsFileOutsideSourceRoot()
    {
        using var tmp = new TemporaryDirectory();
        string source = tmp.CreateSubdirectory("source");
        string outsideFile = Path.Combine(tmp.RootPath, "outside.json");
        File.WriteAllText(outsideFile, "{}");

        var (code, _, _) = RunTool("create", "--source", source, "--file", outsideFile, "--namespace", "game");

        Assert.Equal(2, code);
        Assert.False(File.Exists(outsideFile + ".meta"));
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
