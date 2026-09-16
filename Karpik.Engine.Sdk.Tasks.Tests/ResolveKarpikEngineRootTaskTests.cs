using System.Collections;
using Karpik.Engine.Tooling;
using Microsoft.Build.Framework;
using Xunit;

namespace Karpik.Engine.Sdk.Tasks.Tests;

public sealed class ResolveKarpikEngineRootTaskTests
{
    [Fact]
    public void EmptyExplicitRootResolvesSingleExactInstallation()
    {
        using var temporary = new TemporaryDirectory();
        string expectedRoot = CreateInstallation(
            temporary.RootPath,
            "engine-a",
            "0.6.0-local");
        CreateInstallation(
            temporary.RootPath,
            "engine-other",
            "0.7.0-local");
        var engine = new FakeBuildEngine();
        var task = CreateTask(
            engine,
            "0.6.0-local",
            explicitRoot: null,
            temporary.RootPath);

        bool succeeded = task.Execute();

        Assert.True(succeeded);
        Assert.Empty(engine.Errors);
        Assert.Equal(expectedRoot, task.ResolvedRoot);
    }

    [Fact]
    public void MultipleExactInstallationsFailWithoutSelectingOne()
    {
        using var temporary = new TemporaryDirectory();
        CreateInstallation(temporary.RootPath, "engine-a", "0.6.0-local");
        CreateInstallation(temporary.RootPath, "engine-b", "0.6.0-local");
        var engine = new FakeBuildEngine();
        var task = CreateTask(
            engine,
            "0.6.0-local",
            explicitRoot: null,
            temporary.RootPath);

        bool succeeded = task.Execute();

        Assert.False(succeeded);
        BuildErrorEventArgs error = Assert.Single(engine.Errors);
        Assert.Equal("KARPIK009", error.Code);
        Assert.Contains("Multiple valid engine installations", error.Message, StringComparison.Ordinal);
        Assert.Equal(string.Empty, task.ResolvedRoot);
    }

    [Fact]
    public void ExplicitRootTakesPrecedenceOverAutomaticStore()
    {
        using var temporary = new TemporaryDirectory();
        CreateInstallation(
            temporary.RootPath,
            "automatic",
            "0.6.0-local");
        string explicitRoot = CreateInstallation(
            Path.Combine(temporary.RootPath, "explicit-local"),
            "explicit",
            "0.6.0-local");
        var engine = new FakeBuildEngine();
        var task = CreateTask(
            engine,
            "0.6.0-local",
            explicitRoot,
            temporary.RootPath);

        bool succeeded = task.Execute();

        Assert.True(succeeded);
        Assert.Empty(engine.Errors);
        Assert.Equal(explicitRoot, task.ResolvedRoot);
    }

    [Fact]
    public void MissingAutomaticStoreProducesStableDiagnostic()
    {
        using var temporary = new TemporaryDirectory();
        var engine = new FakeBuildEngine();
        var task = CreateTask(
            engine,
            "0.6.0-local",
            explicitRoot: null,
            Path.Combine(temporary.RootPath, "missing-local"));

        bool succeeded = task.Execute();

        Assert.False(succeeded);
        BuildErrorEventArgs error = Assert.Single(engine.Errors);
        Assert.Equal("KARPIK009", error.Code);
        Assert.Contains("installation store does not exist", error.Message, StringComparison.Ordinal);
        Assert.Equal(string.Empty, task.ResolvedRoot);
    }

    [Fact]
    public void InvalidExplicitRootDoesNotFallBackToAutomaticStore()
    {
        using var temporary = new TemporaryDirectory();
        CreateInstallation(
            temporary.RootPath,
            "automatic",
            "0.6.0-local");
        string missingExplicitRoot = Path.Combine(temporary.RootPath, "missing-explicit");
        var engine = new FakeBuildEngine();
        var task = CreateTask(
            engine,
            "0.6.0-local",
            missingExplicitRoot,
            temporary.RootPath);

        bool succeeded = task.Execute();

        Assert.False(succeeded);
        BuildErrorEventArgs error = Assert.Single(engine.Errors);
        Assert.Equal("KARPIK009", error.Code);
        Assert.Contains(
            "KarpikEngineRoot is not a valid installation",
            error.Message,
            StringComparison.Ordinal);
        Assert.Equal(string.Empty, task.ResolvedRoot);
    }

    private static Karpik.Engine.Sdk.Tasks.ResolveKarpikEngineRootTask CreateTask(
        IBuildEngine buildEngine,
        string sdkVersion,
        string? explicitRoot,
        string localApplicationDataRoot)
    {
        return new Karpik.Engine.Sdk.Tasks.ResolveKarpikEngineRootTask
        {
            BuildEngine = buildEngine,
            SdkVersion = sdkVersion,
            ExplicitRoot = explicitRoot,
            LocalApplicationDataRoot = localApplicationDataRoot
        };
    }

    private static string CreateInstallation(
        string localApplicationDataRoot,
        string directoryName,
        string sdkVersion)
    {
        string root = Path.GetFullPath(Path.Combine(
            localApplicationDataRoot,
            "Karpik",
            "Engines",
            directoryName));
        Directory.CreateDirectory(Path.Combine(root, "editor"));
        Directory.CreateDirectory(Path.Combine(root, "sdk"));
        Directory.CreateDirectory(Path.Combine(root, "runners", "client"));
        Directory.CreateDirectory(Path.Combine(root, "runners", "server"));
        Directory.CreateDirectory(Path.Combine(root, "modules", "TestModule"));
        Directory.CreateDirectory(Path.Combine(root, "native"));
        Directory.CreateDirectory(Path.Combine(root, "shared"));
        File.WriteAllText(Path.Combine(root, "editor", "Karpik.Editor.dll"), "editor");
        File.WriteAllText(Path.Combine(root, "sdk", "Karpik.Engine.Sdk.nupkg"), "sdk");
        File.WriteAllText(
            Path.Combine(root, "runners", "client", "Karpik.Engine.Core.Runner.dll"),
            "client");
        File.WriteAllText(
            Path.Combine(root, "runners", "server", "Karpik.Engine.Core.Runner.dll"),
            "server");
        File.WriteAllText(
            Path.Combine(root, "modules", "TestModule", "TestModule.dll"),
            "module");
        File.WriteAllText(
            Path.Combine(root, "modules", EngineModuleCatalog.FileName),
            EngineModuleCatalog.Serialize([
                new EngineModuleCatalogEntry("TestModule", EngineModuleSide.Shared)
            ]));

        var manifest = new EngineInstallationManifest
        {
            EngineVersion = "0.6.0-dev",
            MsBuildSdkVersion = sdkVersion,
            EditorVersion = "0.6.0-dev",
            RuntimeProtocolVersion = EngineInstallationManifest.CurrentRuntimeProtocolVersion,
            LayoutVersion = EngineInstallationManifest.CurrentLayoutVersion,
            ContentHash = EngineContentHash.Compute(root)
        };
        File.WriteAllText(Path.Combine(root, "engine-installation.json"), manifest.ToJson());
        File.WriteAllText(Path.Combine(root, ".complete"), "complete\n");
        return root;
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            RootPath = Path.Combine(
                Path.GetTempPath(),
                "karpik-sdk-task-tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(RootPath);
        }

        public string RootPath { get; }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(RootPath))
                {
                    Directory.Delete(RootPath, recursive: true);
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    private sealed class FakeBuildEngine : IBuildEngine
    {
        public List<BuildErrorEventArgs> Errors { get; } = [];

        public bool ContinueOnError => false;
        public int LineNumberOfTaskNode => 0;
        public int ColumnNumberOfTaskNode => 0;
        public string ProjectFileOfTaskNode => string.Empty;

        public void LogErrorEvent(BuildErrorEventArgs e) => Errors.Add(e);
        public void LogWarningEvent(BuildWarningEventArgs e) { }
        public void LogMessageEvent(BuildMessageEventArgs e) { }
        public void LogCustomEvent(CustomBuildEventArgs e) { }

        public bool BuildProjectFile(
            string projectFileName,
            string[] targetNames,
            IDictionary globalProperties,
            IDictionary targetOutputs) => throw new NotSupportedException();
    }
}
