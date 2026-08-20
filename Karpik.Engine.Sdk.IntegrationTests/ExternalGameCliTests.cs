using System.Collections.Concurrent;
using System.Diagnostics;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using Karpik.Engine.Core;
using Karpik.Engine.Packager;
using Karpik.Engine.Tooling;
using Xunit;

namespace Karpik.Engine.Sdk.IntegrationTests;

public sealed class ExternalGameCliTests
{
    private const string PackageVersion = "0.6.0-local";
    private static readonly TimeSpan CommandTimeout = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan TerminationTimeout = TimeSpan.FromSeconds(15);
    private readonly ITestOutputHelper _output;
    private readonly List<StartedProcess> _startedProcesses = [];

    public ExternalGameCliTests(ITestOutputHelper output) => _output = output;

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Installed_runner_composes_engine_and_game_modules_for_editor_snapshot()
    {
        string? engineRoot = Environment.GetEnvironmentVariable("KARPIK_TEST_ENGINE_ROOT");
        string? gameRoot = Environment.GetEnvironmentVariable("KARPIK_TEST_GAME_ROOT");
        Assert.SkipUnless(
            !string.IsNullOrWhiteSpace(engineRoot) && !string.IsNullOrWhiteSpace(gameRoot),
            "Set KARPIK_TEST_ENGINE_ROOT and KARPIK_TEST_GAME_ROOT to run the installed runtime composition test.");

        string bundle = Path.Combine(
            gameRoot!, "Source", "KarpikEngineGame.Server", "bin", "Debug", "net10.0", "karpik-bundle");
        string runner = Path.Combine(
            engineRoot!, "runners", "server",
            OperatingSystem.IsWindows() ? "Karpik.Engine.Core.Runner.exe" : "Karpik.Engine.Core.Runner");
        var output = new ConcurrentQueue<string>();
        using var controller = new EditorPreviewController(
            new RuntimeLaunchOptions(Side.Server, runner, bundle, engineRoot));
        controller.OutputReceived += output.Enqueue;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));

        try
        {
            await controller.StartAsync(timeout.Token);
            EditorRuntimeSnapshot? snapshot = await controller.RequestSnapshotAsync(TimeSpan.FromSeconds(5), timeout.Token);

            Assert.NotNull(snapshot);
            Assert.DoesNotContain(output, line => line.Contains("Not found service DCFApixels.DragonECS.EcsDefaultWorld", StringComparison.Ordinal));
            Assert.DoesNotContain(output, line => line.Contains("Engine crashed", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            if (controller.State != EditorPreviewState.Stopped)
                await controller.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public void Template_has_the_standard_external_game_structure()
    {
        string templateRoot = GetTemplateRoot();
        string[] expectedFiles =
        [
            ".template.config/template.json",
            "global.json",
            "KarpikGame.slnx",
            "Directory.Solution.targets",
            "Source/KarpikGame.Client/KarpikGame.Client.csproj",
            "Source/KarpikGame.Client/Content/runtime.txt",
            "Source/KarpikGame.Client.Launcher/KarpikGame.Client.Launcher.csproj",
            "Source/KarpikGame.Client.Launcher/Program.cs",
            "Source/KarpikGame.Server/KarpikGame.Server.csproj",
            "Source/KarpikGame.Server/Content/runtime.txt",
            "Source/KarpikGame.Server.Launcher/KarpikGame.Server.Launcher.csproj",
            "Source/KarpikGame.Server.Launcher/Program.cs",
            "Source/KarpikGame.Shared/KarpikGame.Shared.csproj",
            "Source/KarpikGame.Shared/Content/shared-runtime.txt",
            "Mods/MyCoolMod/mod_info.json",
            "Mods/MyCoolMod/Client/Client.lua",
            "Mods/MyCoolMod/Server/ServerSide.lua",
            "Tests/KarpikGame.Tests/KarpikGame.Tests.csproj"
        ];

        Assert.All(expectedFiles, relativePath =>
            Assert.True(File.Exists(Path.Combine(templateRoot, Normalize(relativePath))), $"Missing template file: {relativePath}"));

        string[] forbiddenNames = [".karpik", "Directory.Build.props", "Directory.Build.targets"];
        Assert.DoesNotContain(
            EnumerateTemplateSourceFiles(templateRoot),
            path => forbiddenNames.Contains(Path.GetFileName(path), StringComparer.OrdinalIgnoreCase));
    }

    [Fact]
    public void Template_launchers_are_runnable_projects_with_build_only_runtime_dependencies()
    {
        string templateRoot = GetTemplateRoot();
        var expected = new Dictionary<string, (string Side, string RuntimeReference)>(StringComparer.Ordinal)
        {
            ["Source/KarpikGame.Client.Launcher/KarpikGame.Client.Launcher.csproj"] =
                ("Client", "..\\KarpikGame.Client\\KarpikGame.Client.csproj"),
            ["Source/KarpikGame.Server.Launcher/KarpikGame.Server.Launcher.csproj"] =
                ("Server", "..\\KarpikGame.Server\\KarpikGame.Server.csproj")
        };

        XDocument solution = XDocument.Load(Path.Combine(templateRoot, "KarpikGame.slnx"));
        string[] solutionProjects = solution
            .Descendants("Project")
            .Select(project => (string?)project.Attribute("Path"))
            .OfType<string>()
            .ToArray();

        foreach ((string relativePath, (string side, string runtimeReference)) in expected)
        {
            Assert.Contains(relativePath, solutionProjects);

            XDocument document = XDocument.Load(Path.Combine(templateRoot, Normalize(relativePath)));
            XElement root = Assert.IsType<XElement>(document.Root);
            Assert.Equal("Karpik.Engine.Sdk", (string?)root.Attribute("Sdk"));
            Assert.Equal("Exe", ReadTopLevelProperty(root, "OutputType"));
            Assert.Equal("Tool", ReadTopLevelProperty(root, "KarpikProjectKind"));
            Assert.Equal(side, ReadTopLevelProperty(root, "KarpikSide"));

            XElement reference = Assert.Single(root.Elements("ItemGroup").Elements("ProjectReference"));
            Assert.Equal(runtimeReference, (string?)reference.Attribute("Include"));
            Assert.Equal("false", (string?)reference.Attribute("ReferenceOutputAssembly"));
            Assert.Equal("false", (string?)reference.Attribute("Private"));
        }
    }

    [Fact]
    public void Template_projects_use_the_karpik_sdk_and_only_legal_static_references()
    {
        string templateRoot = GetTemplateRoot();
        var expected = new Dictionary<string, (string Kind, string Side, string[] References)>(StringComparer.Ordinal)
        {
            ["Source/KarpikGame.Client/KarpikGame.Client.csproj"] =
                ("Runtime", "Client", ["..\\KarpikGame.Shared\\KarpikGame.Shared.csproj"]),
            ["Source/KarpikGame.Server/KarpikGame.Server.csproj"] =
                ("Runtime", "Server", ["..\\KarpikGame.Shared\\KarpikGame.Shared.csproj"]),
            ["Source/KarpikGame.Shared/KarpikGame.Shared.csproj"] =
                ("Runtime", "Shared", []),
            ["Tests/KarpikGame.Tests/KarpikGame.Tests.csproj"] =
                ("Test", "Client", [
                    "..\\..\\Source\\KarpikGame.Client\\KarpikGame.Client.csproj",
                    "..\\..\\Source\\KarpikGame.Shared\\KarpikGame.Shared.csproj"
                ])
        };

        foreach ((string relativePath, (string kind, string side, string[] references)) in expected)
        {
            XDocument document = XDocument.Load(Path.Combine(templateRoot, Normalize(relativePath)));
            XElement root = Assert.IsType<XElement>(document.Root);
            Assert.Equal("Karpik.Engine.Sdk", (string?)root.Attribute("Sdk"));
            Assert.Equal(kind, ReadTopLevelProperty(root, "KarpikProjectKind"));
            Assert.Equal(side, ReadTopLevelProperty(root, "KarpikSide"));

            XElement[] projectReferences = root.Elements("ItemGroup").Elements("ProjectReference").ToArray();
            Assert.All(projectReferences, reference =>
            {
                Assert.NotNull(reference.Attribute("Include"));
                Assert.Null(reference.Attribute("Condition"));
                Assert.Null(reference.Parent?.Attribute("Condition"));
                string include = (string)reference.Attribute("Include")!;
                Assert.DoesNotContain("$(", include, StringComparison.Ordinal);
                Assert.DoesNotContain("@(", include, StringComparison.Ordinal);
                Assert.DoesNotContain("%(", include, StringComparison.Ordinal);
                Assert.DoesNotContain("*", include, StringComparison.Ordinal);
                Assert.DoesNotContain("?", include, StringComparison.Ordinal);
            });
            Assert.Equal(references, projectReferences.Select(reference => (string)reference.Attribute("Include")!).ToArray());
        }
    }

    [Fact]
    public void Template_metadata_pins_the_sdk_and_supports_safe_source_name_replacement()
    {
        string templateRoot = GetTemplateRoot();
        using JsonDocument globalJson = JsonDocument.Parse(File.ReadAllText(Path.Combine(templateRoot, "global.json")));
        Assert.Equal("10.0.100", globalJson.RootElement.GetProperty("sdk").GetProperty("version").GetString());
        Assert.Equal("latestPatch", globalJson.RootElement.GetProperty("sdk").GetProperty("rollForward").GetString());
        Assert.Equal(PackageVersion,
            globalJson.RootElement.GetProperty("msbuild-sdks").GetProperty("Karpik.Engine.Sdk").GetString());

        using JsonDocument metadata = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(templateRoot, ".template.config", "template.json")));
        Assert.Equal("KarpikGame", metadata.RootElement.GetProperty("sourceName").GetString());
        Assert.Equal("karpik-game", metadata.RootElement.GetProperty("shortName").GetString());
        Assert.True(metadata.RootElement.GetProperty("preferNameDirectory").GetBoolean());

        string repositoryRoot = GetRepositoryRoot();
        foreach (string path in EnumerateTemplateSourceFiles(templateRoot))
        {
            string content = File.ReadAllText(path);
            Assert.DoesNotContain(repositoryRoot, content, PathComparison);
            Assert.DoesNotContain("KarpikEngine.csproj", content, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("..\\..\\KarpikEngine", content, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task RuntimeBundle_external_template_supports_ordinary_cli_workflow_and_validation_precedence()
    {
        Assert.SkipUnless(
            Environment.GetEnvironmentVariable("KARPIK_RUN_EXTERNAL_SDK_INTEGRATION") == "1",
            "Set KARPIK_RUN_EXTERNAL_SDK_INTEGRATION=1 to run the external SDK subprocess suite.");

        _startedProcesses.Clear();

        string repositoryRoot = GetRepositoryRoot();
        string templateRoot = GetTemplateRoot();
        string temporaryRoot = Path.Combine(Path.GetTempPath(), $"KarpikExternalSdk_{Guid.NewGuid():N}");
        Assert.False(IsWithinRoot(temporaryRoot, repositoryRoot),
            $"External integration root must be outside the engine repository: {temporaryRoot}");
        Directory.CreateDirectory(temporaryRoot);
        bool cleanupAllowed = true;

        try
        {
            string retainedEngineRoot = ResolveRetainedEngineRoot(repositoryRoot);
            string packageFeed = Path.Combine(repositoryRoot, "artifacts", "nuget");
            Directory.CreateDirectory(packageFeed);
            string offlinePackageFeed = Path.Combine(temporaryRoot, "offline-packages");
            SeedOfflinePackageFeed(repositoryRoot, offlinePackageFeed);
            string hive = Path.Combine(temporaryRoot, "template-hive");
            string dotnetHome = Path.Combine(temporaryRoot, "dotnet-home");
            string nugetPackages = Path.Combine(temporaryRoot, "nuget-packages");
            string nugetHttpCache = Path.Combine(temporaryRoot, "nuget-http-cache");
            string nugetConfig = Path.Combine(temporaryRoot, "NuGet.Config");
            WriteNuGetConfig(temporaryRoot, packageFeed, offlinePackageFeed);
            var commonEnvironment = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
            {
                ["DOTNET_CLI_HOME"] = dotnetHome,
                ["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1",
                ["DOTNET_NOLOGO"] = "1",
                ["DOTNET_CLI_USE_MSBUILD_SERVER"] = "0",
                ["MSBUILDDISABLENODEREUSE"] = "1",
                ["NUGET_PACKAGES"] = nugetPackages,
                ["NUGET_HTTP_CACHE_PATH"] = nugetHttpCache,
                ["NuGetAudit"] = "false",
                ["RestoreDisableParallel"] = "true",
                ["KarpikEngineRoot"] = retainedEngineRoot
            };
            string engineRoot = await CreateUpdatedEngineInstallationAsync(
                repositoryRoot,
                retainedEngineRoot,
                temporaryRoot,
                nugetConfig,
                commonEnvironment);
            commonEnvironment.Remove("KarpikEngineRoot");
            commonEnvironment["KarpikLocalApplicationDataRoot"] =
                Path.Combine(temporaryRoot, "local");
            Assert.False(commonEnvironment.ContainsKey("KarpikEngineRoot"));
            Assert.True(File.Exists(Path.Combine(
                repositoryRoot,
                "Karpik.Engine.Sdk.Tasks",
                "bin",
                "Debug",
                "net10.0",
                "Karpik.Engine.Sdk.Tasks.dll")),
                "The integration project build must prepare SDK task outputs before isolated packing.");

            ProcessResult pack = await RunAsync(
                repositoryRoot,
                ["pack", "Karpik.Engine.Sdk\\Karpik.Engine.Sdk.csproj", "-c", "Debug", "-m:1", "-nr:false",
                    "--no-restore", "--no-build", $"-p:PackageVersion={PackageVersion}", $"-p:RestoreConfigFile={nugetConfig}", "-o", packageFeed],
                commonEnvironment);
            AssertSuccess(pack, "pack the current Karpik.Engine.Sdk package");
            Assert.True(File.Exists(Path.Combine(packageFeed, $"Karpik.Engine.Sdk.{PackageVersion}.nupkg")));

            ProcessResult install = await RunAsync(
                temporaryRoot,
                ["new", "--debug:custom-hive", hive, "install", templateRoot],
                commonEnvironment);
            AssertSuccess(install, "install the Karpik game template into an isolated hive");

            string renamedRoot = Path.Combine(temporaryRoot, "renamed", "MilestoneFourGame");
            string repeatedRenamedRoot = Path.Combine(temporaryRoot, "renamed-repeat", "MilestoneFourGame");
            await MaterializeAsync(
                temporaryRoot, hive, renamedRoot, "MilestoneFourGame", commonEnvironment);
            await MaterializeAsync(
                temporaryRoot, hive, repeatedRenamedRoot, "MilestoneFourGame", commonEnvironment);
            AssertRenamedMaterializationsAreDeterministic(
                renamedRoot, repeatedRenamedRoot, "MilestoneFourGame", repositoryRoot);

            string validRoot = Path.Combine(temporaryRoot, "valid", "KarpikGame");
            await MaterializeAsync(temporaryRoot, hive, validRoot, "KarpikGame", commonEnvironment);
            AssertGeneratedSourceTree(validRoot, repositoryRoot);
            WriteNuGetConfig(validRoot, packageFeed, offlinePackageFeed);

            string invalidSdkRoot = Path.Combine(temporaryRoot, "invalid-sdk", "KarpikGame");
            string invalidSideRoot = Path.Combine(temporaryRoot, "invalid-side", "KarpikGame");
            CopyDirectory(validRoot, invalidSdkRoot);
            CopyDirectory(validRoot, invalidSideRoot);

            ProcessResult restore = await RunAsync(
                validRoot,
                ["restore", "KarpikGame.slnx", "-m:1", "-nr:false"],
                commonEnvironment);
            AssertSuccess(restore, "restore the generated solution");
            ProcessResult build = await RunAsync(
                validRoot,
                ["build", "KarpikGame.slnx", "-m:1", "-nr:false", "--no-restore"],
                commonEnvironment);
            AssertSuccess(build, "build the generated solution");
            AssertRuntimeBundles(validRoot, engineRoot, "KarpikGame");
            await AssertRunnerHotReloadAndCleanShutdownAsync(validRoot, engineRoot, "KarpikGame");
            await AssertMultiWorkerEcsCycleAsync(validRoot, engineRoot, "KarpikGame");

            string secondRoot = Path.Combine(temporaryRoot, "second", "SecondGame");
            await MaterializeAsync(temporaryRoot, hive, secondRoot, "SecondGame", commonEnvironment);
            WriteNuGetConfig(secondRoot, packageFeed, offlinePackageFeed);
            ProcessResult secondRestore = await RunAsync(
                secondRoot, ["restore", "SecondGame.slnx", "-m:1", "-nr:false"], commonEnvironment);
            AssertSuccess(secondRestore, "restore second independently generated game");
            ProcessResult secondBuild = await RunAsync(
                secondRoot, ["build", "SecondGame.slnx", "-m:1", "-nr:false", "--no-restore"], commonEnvironment);
            AssertSuccess(secondBuild, "build second independently generated game");
            AssertRuntimeBundles(secondRoot, engineRoot, "SecondGame");
            await AssertMultiWorkerEcsCycleAsync(secondRoot, engineRoot, "SecondGame");

            await AssertStaticSharedProjectCompilesAgainstInstalledModulesAsync(
                secondRoot,
                "SecondGame",
                commonEnvironment);

            ProcessResult test = await RunAsync(
                validRoot,
                ["test", "KarpikGame.slnx", "-m:1", "-nr:false", "--no-build"],
                commonEnvironment);
            AssertSuccess(test, "test the generated solution");
            ProcessResult publish = await RunAsync(
                validRoot,
                ["publish", "Source\\KarpikGame.Client\\KarpikGame.Client.csproj", "-m:1", "-nr:false", "--no-restore"],
                commonEnvironment);
            AssertSuccess(publish, "publish the generated client");
            AssertOutputsAreExternalAndPortable(validRoot, repositoryRoot);

            await AssertDynamicSharedProjectDoesNotReceiveStaticModuleReferencesAsync(
                validRoot,
                "KarpikGame",
                commonEnvironment);
            await AssertStaticSideBoundariesAsync(
                temporaryRoot,
                hive,
                packageFeed,
                offlinePackageFeed,
                commonEnvironment);

            await AssertMissingSdkFailsBeforeCompilationAsync(
                invalidSdkRoot, packageFeed, offlinePackageFeed, commonEnvironment);
            await AssertForbiddenSideFailsBeforeCompilationAsync(
                invalidSideRoot, packageFeed, offlinePackageFeed, commonEnvironment);
            AssertAllSubprocessesUseOwnedState(temporaryRoot);
        }
        catch (ExternalProcessTerminationException)
        {
            cleanupAllowed = false;
            throw;
        }
        finally
        {
            if (cleanupAllowed)
            {
                DeleteOwnedTemporaryRoot(temporaryRoot);
            }
        }
    }

    private async Task MaterializeAsync(
        string workingDirectory,
        string hive,
        string gameRoot,
        string gameName,
        IReadOnlyDictionary<string, string?> environment)
    {
        ProcessResult materialize = await RunAsync(
            workingDirectory,
            ["new", "--debug:custom-hive", hive, "karpik-game", "--name", gameName, "--output", gameRoot],
            environment);
        AssertSuccess(materialize, "materialize the Karpik game template");
        Assert.True(File.Exists(Path.Combine(gameRoot, $"{gameName}.slnx")), materialize.CombinedOutput);
    }

    private async Task AssertStaticSharedProjectCompilesAgainstInstalledModulesAsync(
        string gameRoot,
        string gameName,
        IReadOnlyDictionary<string, string?> environment)
    {
        string project = Path.Combine(gameRoot, "Source", $"{gameName}.Shared", $"{gameName}.Shared.csproj");
        SetCompositionMode(project, "Static");
        File.WriteAllText(
            Path.Combine(gameRoot, "Source", $"{gameName}.Shared", "StaticTransform2DProbe.cs"),
            """
            using Karpik.Engine.Shared.Spatial2D;

            namespace StaticCompositionProbe;

            public struct Transform2DProbe
            {
                public Transform2D Value;
            }
            """);

        ProcessResult build = await RunAsync(
            gameRoot,
            ["build", project, "-m:1", "-nr:false", "--no-restore"],
            environment);
        AssertSuccess(build, "compile a Static Shared project against Karpik.Engine.Shared.Spatial2D.Transform2D without a manual reference");
    }

    private async Task AssertDynamicSharedProjectDoesNotReceiveStaticModuleReferencesAsync(
        string gameRoot,
        string gameName,
        IReadOnlyDictionary<string, string?> environment)
    {
        string project = Path.Combine(gameRoot, "Source", $"{gameName}.Shared", $"{gameName}.Shared.csproj");
        File.WriteAllText(
            Path.Combine(gameRoot, "Source", $"{gameName}.Shared", "DynamicTransform2DProbe.cs"),
            """
            using Karpik.Engine.Shared.Spatial2D;

            namespace DynamicCompositionProbe;

            public struct Transform2DProbe
            {
                public Transform2D Value;
            }
            """);

        ProcessResult build = await RunAsync(
            gameRoot,
            ["build", project, "-m:1", "-nr:false", "--no-restore"],
            environment);
        AssertMissingReference(build);
    }

    private async Task AssertStaticSideBoundariesAsync(
        string temporaryRoot,
        string hive,
        string packageFeed,
        string offlinePackageFeed,
        IReadOnlyDictionary<string, string?> environment)
    {
        (string side, string source)[] fixtures =
        [
            ("Shared", "using Karpik.Engine.Client.InputModule; public class SideProbe { private Input Value = null!; }"),
            ("Client", "using Network.Server.LiteNetLib; public class SideProbe { private NetworkServerModuleInstaller Value = null!; }"),
            ("Server", "using Karpik.Engine.Client.InputModule; public class SideProbe { private Input Value = null!; }")
        ];

        foreach ((string side, string source) in fixtures)
        {
            string gameName = $"Static{side}Boundary";
            string gameRoot = Path.Combine(temporaryRoot, "static-boundaries", gameName);
            await MaterializeAsync(temporaryRoot, hive, gameRoot, gameName, environment);
            WriteNuGetConfig(gameRoot, packageFeed, offlinePackageFeed);
            string project = Path.Combine(gameRoot, "Source", $"{gameName}.{side}", $"{gameName}.{side}.csproj");
            SetCompositionMode(project, "Static");
            File.WriteAllText(Path.Combine(Path.GetDirectoryName(project)!, "ForbiddenSideProbe.cs"), source);

            ProcessResult restore = await RunAsync(
                gameRoot,
                ["restore", project, "-m:1", "-nr:false"],
                environment);
            AssertSuccess(restore, $"restore the Static {side} side-boundary fixture");
            ProcessResult build = await RunAsync(
                gameRoot,
                ["build", project, "-m:1", "-nr:false", "--no-restore"],
                environment);
            AssertMissingReference(build);
        }
    }

    private static void SetCompositionMode(string projectPath, string mode)
    {
        XDocument document = XDocument.Load(projectPath, LoadOptions.PreserveWhitespace);
        XElement root = Assert.IsType<XElement>(document.Root);
        XElement propertyGroup = root.Elements("PropertyGroup").First();
        propertyGroup.Add(new XElement("KarpikCompositionMode", mode));
        document.Save(projectPath);
    }

    private static void AssertMissingReference(ProcessResult result)
    {
        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("error CS", result.CombinedOutput, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("KARPIK011", result.CombinedOutput, StringComparison.Ordinal);
        Assert.DoesNotContain("Unhandled exception", result.CombinedOutput, StringComparison.OrdinalIgnoreCase);
    }

    private static void AssertRenamedMaterializationsAreDeterministic(
        string firstRoot,
        string secondRoot,
        string gameName,
        string repositoryRoot)
    {
        AssertGeneratedSourceTree(firstRoot, repositoryRoot);
        AssertGeneratedSourceTree(secondRoot, repositoryRoot);
        string[] firstFiles = Directory.EnumerateFiles(firstRoot, "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(firstRoot, path))
            .Order(StringComparer.Ordinal)
            .ToArray();
        string[] secondFiles = Directory.EnumerateFiles(secondRoot, "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(secondRoot, path))
            .Order(StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(firstFiles, secondFiles);
        Assert.DoesNotContain(firstFiles, path => path.Contains("KarpikGame", StringComparison.Ordinal));
        Assert.Contains($"{gameName}.slnx", firstFiles);

        foreach (string relativePath in firstFiles)
        {
            string firstPath = Path.Combine(firstRoot, relativePath);
            string secondPath = Path.Combine(secondRoot, relativePath);
            Assert.Equal(File.ReadAllBytes(firstPath), File.ReadAllBytes(secondPath));
            Assert.DoesNotContain("KarpikGame", File.ReadAllText(firstPath), StringComparison.Ordinal);
        }
    }

    private async Task AssertMissingSdkFailsBeforeCompilationAsync(
        string gameRoot,
        string packageFeed,
        string offlinePackageFeed,
        IReadOnlyDictionary<string, string?> environment)
    {
        WriteNuGetConfig(gameRoot, packageFeed, offlinePackageFeed);
        ProcessResult restore = await RunAsync(
            gameRoot,
            ["restore", "KarpikGame.slnx", "-m:1", "-nr:false"],
            environment);
        AssertSuccess(restore, "restore the valid base for the KARPIK001 mutation");

        string projectPath = Path.Combine(gameRoot, "Source", "KarpikGame.Client", "KarpikGame.Client.csproj");
        File.WriteAllText(projectPath,
            File.ReadAllText(projectPath).Replace("Sdk=\"Karpik.Engine.Sdk\"", "Sdk=\"Microsoft.NET.Sdk\"", StringComparison.Ordinal));
        File.WriteAllText(Path.Combine(gameRoot, "Source", "KarpikGame.Client", "CompilerMustNotRun.cs"),
            "#error KARPIK001_COMPILER_MARKER_MUST_NOT_RUN\n");

        ProcessResult result = await RunAsync(
            gameRoot,
            ["build", "KarpikGame.slnx", "-m:1", "-nr:false", "--no-restore"],
            environment);
        AssertDiagnosticBeforeCompilation(result, "KARPIK001", "KARPIK001_COMPILER_MARKER_MUST_NOT_RUN");
    }

    private async Task AssertForbiddenSideFailsBeforeCompilationAsync(
        string gameRoot,
        string packageFeed,
        string offlinePackageFeed,
        IReadOnlyDictionary<string, string?> environment)
    {
        WriteNuGetConfig(gameRoot, packageFeed, offlinePackageFeed);
        ProcessResult restore = await RunAsync(
            gameRoot,
            ["restore", "KarpikGame.slnx", "-m:1", "-nr:false"],
            environment);
        AssertSuccess(restore, "restore the valid base for the KARPIK005 mutation");

        string projectPath = Path.Combine(gameRoot, "Source", "KarpikGame.Client", "KarpikGame.Client.csproj");
        XDocument document = XDocument.Load(projectPath, LoadOptions.PreserveWhitespace);
        XElement root = Assert.IsType<XElement>(document.Root);
        XElement itemGroup = root.Elements("ItemGroup").First(group => group.Elements("ProjectReference").Any());
        itemGroup.Add(new XElement("ProjectReference",
            new XAttribute("Include", "..\\KarpikGame.Server\\KarpikGame.Server.csproj")));
        document.Save(projectPath);
        File.WriteAllText(Path.Combine(gameRoot, "Source", "KarpikGame.Client", "CompilerMustNotRun.cs"),
            "#error KARPIK005_COMPILER_MARKER_MUST_NOT_RUN\n");

        ProcessResult solutionResult = await RunAsync(
            gameRoot,
            ["build", "KarpikGame.slnx", "-m:1", "-nr:false", "--no-restore"],
            environment);
        AssertDiagnosticBeforeCompilation(solutionResult, "KARPIK005", "KARPIK005_COMPILER_MARKER_MUST_NOT_RUN");

        ProcessResult projectResult = await RunAsync(
            gameRoot,
            ["build", "Source\\KarpikGame.Client\\KarpikGame.Client.csproj", "-m:1", "-nr:false", "--no-restore"],
            environment);
        AssertDiagnosticBeforeCompilation(projectResult, "KARPIK005", "KARPIK005_COMPILER_MARKER_MUST_NOT_RUN");
    }

    private static void AssertDiagnosticBeforeCompilation(ProcessResult result, string diagnostic, string marker)
    {
        Assert.NotEqual(0, result.ExitCode);
        int diagnosticIndex = result.CombinedOutput.IndexOf(diagnostic, StringComparison.Ordinal);
        Assert.True(diagnosticIndex >= 0, result.CombinedOutput);
        Assert.DoesNotContain(marker, result.CombinedOutput, StringComparison.Ordinal);
        Assert.DoesNotContain("error CS", result.CombinedOutput, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Unhandled exception", result.CombinedOutput, StringComparison.OrdinalIgnoreCase);
    }

    private static void AssertGeneratedSourceTree(string gameRoot, string repositoryRoot)
    {
        Assert.False(IsWithinRoot(gameRoot, repositoryRoot));
        string[] forbiddenNames = [".karpik", "Directory.Build.props", "Directory.Build.targets"];
        string[] files = Directory.EnumerateFiles(gameRoot, "*", SearchOption.AllDirectories).ToArray();
        Assert.DoesNotContain(files,
            path => forbiddenNames.Contains(Path.GetFileName(path), StringComparer.OrdinalIgnoreCase));
        Assert.All(files, path => Assert.DoesNotContain(repositoryRoot, File.ReadAllText(path), PathComparison));
    }

    private static void AssertOutputsAreExternalAndPortable(string gameRoot, string repositoryRoot)
    {
        string[] outputFiles = Directory.EnumerateFiles(gameRoot, "*", SearchOption.AllDirectories)
            .Where(path => Path.GetRelativePath(gameRoot, path).Split(Path.DirectorySeparatorChar)
                .Any(part => part is "bin" or "publish"))
            .ToArray();
        Assert.NotEmpty(outputFiles);
        Assert.All(outputFiles, path => Assert.False(IsWithinRoot(path, repositoryRoot)));

        foreach (string dependencyFile in outputFiles.Where(path =>
                     Path.GetExtension(path).Equals(".json", StringComparison.OrdinalIgnoreCase)))
        {
            Assert.DoesNotContain(repositoryRoot, File.ReadAllText(dependencyFile), PathComparison);
        }
    }

    private static void AssertRuntimeBundles(string gameRoot, string engineRoot, string generatedProjectName)
    {
        string output = Path.Combine("bin", "Debug", "net10.0", "karpik-bundle");
        string client = Path.Combine(gameRoot, "Source", $"{generatedProjectName}.Client", output);
        string server = Path.Combine(gameRoot, "Source", $"{generatedProjectName}.Server", output);
        string shared = Path.Combine(gameRoot, "Source", $"{generatedProjectName}.Shared", output);
        Assert.True(Directory.Exists(client), $"Client bundle is missing: {client}");
        Assert.True(Directory.Exists(server), $"Server bundle is missing: {server}");
        Assert.False(Directory.Exists(shared), $"Shared project must not create a runtime bundle: {shared}");
        Assert.False(IsWithinRoot(client, engineRoot));
        Assert.False(IsWithinRoot(server, engineRoot));
        AssertBundle(
            client,
            "Client",
            $"{generatedProjectName}.Client.dll",
            $"{generatedProjectName}.Server.dll",
            $"{generatedProjectName}.Shared.dll");
        AssertBundle(
            server,
            "Server",
            $"{generatedProjectName}.Server.dll",
            $"{generatedProjectName}.Client.dll",
            $"{generatedProjectName}.Shared.dll");

        static void AssertBundle(string bundle, string side, string primary, string forbidden, string sharedAssembly)
        {
            string outputDirectory = Assert.IsType<string>(Path.GetDirectoryName(bundle));
            string outputMod = Path.Combine(outputDirectory, "Mods", "MyCoolMod", "mod_info.json");
            Assert.True(
                File.Exists(outputMod),
                $"Generated {side} output is missing copied mod metadata: {outputMod}{Environment.NewLine}" +
                string.Join(Environment.NewLine, Directory.EnumerateFiles(outputDirectory, "*", SearchOption.AllDirectories)));

            Assert.Equal($"karpik-runtime-side-v1:{side}\n", File.ReadAllText(Path.Combine(bundle, "runtime-bundle.side")));
            Assert.Equal("karpik-runtime-bundle-v1\n", File.ReadAllText(Path.Combine(bundle, ".complete")));
            string modules = Assert.Single(Directory.GetDirectories(bundle, "modules.version.*", SearchOption.TopDirectoryOnly));
            Assert.Equal("karpik-module-staging-v1\n", File.ReadAllText(Path.Combine(modules, ".complete")));
            string[] names = File.ReadAllLines(Path.Combine(modules, "modules.list"));
            Assert.Equal(new[] { primary, sharedAssembly }.Order(StringComparer.Ordinal), names);
            Assert.DoesNotContain(forbidden, names, StringComparer.OrdinalIgnoreCase);
            Assert.DoesNotContain(names, name => name.StartsWith("Karpik.Engine.Core.Runner", StringComparison.OrdinalIgnoreCase));
            Assert.True(File.Exists(Path.Combine(bundle, "Content", "runtime.txt")));
            Assert.True(File.Exists(Path.Combine(bundle, "Content", "shared-runtime.txt")));
            Assert.True(
                File.Exists(Path.Combine(bundle, "Mods", "MyCoolMod", "mod_info.json")),
                $"Generated {side} bundle is missing mod metadata.{Environment.NewLine}" +
                string.Join(Environment.NewLine, Directory.EnumerateFiles(bundle, "*", SearchOption.AllDirectories)));
            Assert.True(File.Exists(Path.Combine(bundle, "Mods", "MyCoolMod", "Client", "Client.lua")));
            Assert.True(File.Exists(Path.Combine(bundle, "Mods", "MyCoolMod", "Server", "ServerSide.lua")));
        }
    }

    private async Task<string> CreateUpdatedEngineInstallationAsync(
        string repositoryRoot,
        string retainedEngineRoot,
        string temporaryRoot,
        string nugetConfig,
        IReadOnlyDictionary<string, string?> environment)
    {
        string runnerProject = Path.Combine(repositoryRoot, "Karpik.Engine.Core.Runner", "Karpik.Engine.Core.Runner.csproj");
        string ownedBuildRoot = Path.Combine(temporaryRoot, "runner-build");
        string artifacts = Path.Combine(ownedBuildRoot, "artifacts") + Path.DirectorySeparatorChar;
        string extensions = Path.Combine(ownedBuildRoot, "obj") + Path.DirectorySeparatorChar +
                            "$(MSBuildProjectName)" + Path.DirectorySeparatorChar;
        Dictionary<string, FileStamp> before = SnapshotFiles(
            Path.Combine(repositoryRoot, "Karpik.Engine.Core.Runner", "bin"),
            Path.Combine(repositoryRoot, "Karpik.Engine.Core.Runner", "obj"));
        string[] ownedProperties =
        [
            $"-p:RestoreConfigFile={nugetConfig}",
            $"-p:ArtifactsPath={artifacts}",
            $"-p:MSBuildProjectExtensionsPath={extensions}"
        ];
        ProcessResult runnerRestore = await RunAsync(
            repositoryRoot,
            ["restore", runnerProject, "-m:1", "-nr:false", .. ownedProperties],
            environment);
        AssertSuccess(runnerRestore, "restore the current engine-owned runner into transaction-owned state");
        ProcessResult runnerBuild = await RunAsync(
            repositoryRoot,
            ["build", runnerProject, "-c", "Release", "-m:1", "-nr:false", "--no-restore", .. ownedProperties],
            environment);
        AssertSuccess(runnerBuild, "build the current engine-owned runner");
        Assert.Equal(before, SnapshotFiles(
            Path.Combine(repositoryRoot, "Karpik.Engine.Core.Runner", "bin"),
            Path.Combine(repositoryRoot, "Karpik.Engine.Core.Runner", "obj")));

        string prepared = Path.Combine(temporaryRoot, "prepared-task5-engine");
        foreach (string directory in new[] { "editor", "sdk", "modules", "native" })
        {
            CopyDirectory(Path.Combine(retainedEngineRoot, directory), Path.Combine(prepared, directory));
        }
        WriteEngineModuleCatalog(repositoryRoot, Path.Combine(prepared, "modules"));
        string runnerOutput = Assert.Single(Directory.EnumerateFiles(
                artifacts,
                "Karpik.Engine.Core.Runner.runtimeconfig.json",
                SearchOption.AllDirectories)
            .Select(Path.GetDirectoryName)
            .OfType<string>());
        Assert.True(IsWithinRoot(runnerOutput, ownedBuildRoot));
        Assert.True(File.Exists(Path.Combine(runnerOutput, "Karpik.Engine.Core.Runner.dll")));
        CopyDirectory(runnerOutput, Path.Combine(prepared, "runners", "client"));
        CopyDirectory(runnerOutput, Path.Combine(prepared, "runners", "server"));

        EnginePayloadBuildResult published = new EnginePayloadBuilder().Build(
            prepared,
            Path.Combine(temporaryRoot, "local", "Karpik"),
            "0.6.0-dev-task5",
            PackageVersion);
        EngineInstallationValidationResult validation = new EngineInstallationValidator().Validate(
            published.DestinationDirectory,
            PackageVersion,
            "0.6.0-dev-task5");
        Assert.True(validation.IsValid, validation.Message);
        Assert.False(IsWithinRoot(published.DestinationDirectory, retainedEngineRoot));
        return published.DestinationDirectory;
    }

    private static async Task AssertRunnerHotReloadAndCleanShutdownAsync(
        string gameRoot,
        string engineRoot,
        string generatedProjectName)
    {
        string bundle = Path.Combine(
            gameRoot,
            "Source",
            $"{generatedProjectName}.Server",
            "bin",
            "Debug",
            "net10.0",
            "karpik-bundle");
        string runner = Path.Combine(
            engineRoot,
            "runners",
            "server",
            OperatingSystem.IsWindows() ? "Karpik.Engine.Core.Runner.exe" : "Karpik.Engine.Core.Runner");
        Assert.True(File.Exists(runner), $"Engine installation runner is missing: {runner}");
        Assert.False(IsWithinRoot(runner, bundle));
        Dictionary<string, FileStamp> engineBefore = SnapshotFiles(engineRoot);
        var output = new ConcurrentQueue<string>();
        using var controller = new EditorPreviewController(new RuntimeLaunchOptions(Side.Server, runner, bundle, engineRoot));
        controller.OutputReceived += output.Enqueue;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));

        try
        {
            await controller.StartAsync(timeout.Token);
        }
        catch (Exception exception)
        {
            await Task.Delay(100);
            throw new InvalidOperationException(
                $"Engine-installed runner did not reach worker-ready.{Environment.NewLine}" +
                string.Join(Environment.NewLine, output),
                exception);
        }
        Assert.Equal(EditorPreviewState.Running, controller.State);
        int firstProcessId = Assert.IsType<int>(controller.ProcessId);
        await controller.HotReloadAsync(timeout.Token);
        Assert.Equal(EditorPreviewState.Running, controller.State);
        int secondProcessId = Assert.IsType<int>(controller.ProcessId);
        Assert.NotEqual(firstProcessId, secondProcessId);
        string stateRoot = Path.Combine(bundle, "reload", "state");
        Assert.True(!Directory.Exists(stateRoot) || !Directory.EnumerateFileSystemEntries(stateRoot).Any(),
            "Hot reload state must be consumed by the restarted worker.");
        await controller.StopAsync(timeout.Token);

        Assert.Equal(EditorPreviewState.Stopped, controller.State);
        Assert.Null(controller.ProcessId);
        Assert.True(output.Count(line => line.Contains("[Worker] Exited cleanly", StringComparison.Ordinal)) >= 2,
            string.Join(Environment.NewLine, output));
        Assert.Contains(output, line => line.Contains("Total modules with state: 1", StringComparison.Ordinal));
        Assert.DoesNotContain(output, line => line.Contains("Engine crashed", StringComparison.OrdinalIgnoreCase));
        string shadowRoot = Path.Combine(bundle, "reload", "shadow");
        Assert.True(!Directory.Exists(shadowRoot) || !Directory.EnumerateFileSystemEntries(shadowRoot).Any(),
            "Clean stop must remove all bundle-owned worker shadow directories.");
        Assert.Equal(engineBefore, SnapshotFiles(engineRoot));
    }

    private static async Task AssertMultiWorkerEcsCycleAsync(
        string gameRoot,
        string engineRoot,
        string generatedProjectName)
    {
        string serverBundle = Path.Combine(
            gameRoot, "Source", $"{generatedProjectName}.Server", "bin", "Debug", "net10.0", "karpik-bundle");
        string clientBundle = Path.Combine(
            gameRoot, "Source", $"{generatedProjectName}.Client", "bin", "Debug", "net10.0", "karpik-bundle");
        string serverRunner = Path.Combine(
            engineRoot, "runners", "server",
            OperatingSystem.IsWindows() ? "Karpik.Engine.Core.Runner.exe" : "Karpik.Engine.Core.Runner");
        string clientRunner = Path.Combine(
            engineRoot, "runners", "client",
            OperatingSystem.IsWindows() ? "Karpik.Engine.Core.Runner.exe" : "Karpik.Engine.Core.Runner");

        Assert.True(File.Exists(serverRunner), $"Server runner is missing: {serverRunner}");
        Assert.True(File.Exists(clientRunner), $"Client runner is missing: {clientRunner}");

        var serverOutput = new ConcurrentQueue<string>();
        var client1Output = new ConcurrentQueue<string>();
        var client2Output = new ConcurrentQueue<string>();

        using var server = new EditorPreviewController(
            new RuntimeLaunchOptions(Side.Server, serverRunner, serverBundle, engineRoot));
        server.OutputReceived += serverOutput.Enqueue;

        using var client1 = new EditorPreviewController(
            new RuntimeLaunchOptions(Side.Client, clientRunner, clientBundle, engineRoot));
        client1.OutputReceived += client1Output.Enqueue;

        using var client2 = new EditorPreviewController(
            new RuntimeLaunchOptions(Side.Client, clientRunner, clientBundle, engineRoot));
        client2.OutputReceived += client2Output.Enqueue;

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));

        try
        {
            await Task.WhenAll(
                server.StartAsync(timeout.Token),
                client1.StartAsync(timeout.Token),
                client2.StartAsync(timeout.Token));

            Assert.Equal(EditorPreviewState.Running, server.State);
            Assert.Equal(EditorPreviewState.Running, client1.State);
            Assert.Equal(EditorPreviewState.Running, client2.State);

            Assert.Contains(serverOutput,
                line => line.Contains("[ServerGame] Content: server runtime content", StringComparison.Ordinal));
            Assert.Contains(serverOutput,
                line => line.Contains("[ServerGame] Shared content: shared runtime content", StringComparison.Ordinal));
            Assert.Contains(client1Output,
                line => line.Contains("[ClientGame] Content: client runtime content", StringComparison.Ordinal));
            Assert.Contains(client2Output,
                line => line.Contains("[ClientGame] Content: client runtime content", StringComparison.Ordinal));

            EditorRuntimeSnapshot? snapshot = await server.RequestSnapshotAsync(
                TimeSpan.FromSeconds(5), timeout.Token);
            Assert.NotNull(snapshot);
            Assert.True(snapshot.TotalEntityCount > 0,
                $"Expected non-empty ECS world, got TotalEntityCount={snapshot.TotalEntityCount}{Environment.NewLine}" +
                string.Join(Environment.NewLine, serverOutput));
            int beforeCount = snapshot.TotalEntityCount;

            Assert.Contains(snapshot.Entities, entity =>
                entity.Components.Any(component =>
                    component.TypeName.Contains("GameComponent", StringComparison.Ordinal) &&
                    component.DisplayValue.Contains("42", StringComparison.Ordinal)));

            int firstProcessId = Assert.IsType<int>(server.ProcessId);
            await server.HotReloadAsync(timeout.Token);
            int secondProcessId = Assert.IsType<int>(server.ProcessId);
            Assert.NotEqual(firstProcessId, secondProcessId);

            string stateRoot = Path.Combine(serverBundle, "reload", "state");
            Assert.True(!Directory.Exists(stateRoot) || !Directory.EnumerateFileSystemEntries(stateRoot).Any(),
                "Hot reload state must be consumed by the restarted worker.");

            EditorRuntimeSnapshot? snapshotAfter = await server.RequestSnapshotAsync(
                TimeSpan.FromSeconds(5), timeout.Token);
            Assert.NotNull(snapshotAfter);
            Assert.True(snapshotAfter.TotalEntityCount > 0,
                $"Expected non-empty ECS world after hot reload.{Environment.NewLine}" +
                string.Join(Environment.NewLine, serverOutput));

            Assert.True(snapshotAfter.TotalEntityCount == beforeCount + 1,
                $"ECS state loss or entity duplication: before={beforeCount}, after={snapshotAfter.TotalEntityCount}, expected={beforeCount + 1}");

            Assert.Contains(snapshotAfter.Entities, entity =>
                entity.Components.Any(component =>
                    component.TypeName.Contains("GameComponent", StringComparison.Ordinal) &&
                    component.DisplayValue.Contains("42", StringComparison.Ordinal)));

            await Task.WhenAll(
                server.StopAsync(timeout.Token),
                client1.StopAsync(timeout.Token),
                client2.StopAsync(timeout.Token));

            Assert.Equal(EditorPreviewState.Stopped, server.State);
            Assert.Equal(EditorPreviewState.Stopped, client1.State);
            Assert.Equal(EditorPreviewState.Stopped, client2.State);
            Assert.Null(server.ProcessId);
            Assert.Null(client1.ProcessId);
            Assert.Null(client2.ProcessId);

            Assert.DoesNotContain(serverOutput,
                line => line.Contains("Engine crashed", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(client1Output,
                line => line.Contains("Engine crashed", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(client2Output,
                line => line.Contains("Engine crashed", StringComparison.OrdinalIgnoreCase));

            string shadowRoot = Path.Combine(serverBundle, "reload", "shadow");
            Assert.True(!Directory.Exists(shadowRoot) || !Directory.EnumerateFileSystemEntries(shadowRoot).Any(),
                "Clean stop must remove all bundle-owned worker shadow directories.");
        }
        finally
        {
            await Task.WhenAll(
                TryStopAsync(server),
                TryStopAsync(client1),
                TryStopAsync(client2));
        }

        static async Task TryStopAsync(EditorPreviewController controller)
        {
            if (controller.State != EditorPreviewState.Stopped)
            {
                try { await controller.StopAsync(CancellationToken.None); } catch { }
            }
        }
    }

    private static string ResolveRetainedEngineRoot(string repositoryRoot)
    {
        string enginesRoot = Path.Combine(repositoryRoot, "artifacts", "karpik-home-final", "Engines");
        Assert.True(Directory.Exists(enginesRoot), $"Retained Milestone 3 engine store is missing: {enginesRoot}");
        string[] candidates = Directory.EnumerateDirectories(enginesRoot, "*", SearchOption.TopDirectoryOnly)
            .Where(directory => File.Exists(Path.Combine(directory, ".complete")))
            .ToArray();
        string engineRoot = Assert.Single(candidates);
        EngineInstallationManifest manifest = EngineInstallationManifest.Parse(
            File.ReadAllText(Path.Combine(engineRoot, "engine-installation.json")));
        Assert.Equal(PackageVersion, manifest.MsBuildSdkVersion);
        return Path.GetFullPath(engineRoot);
    }

    private static void WriteEngineModuleCatalog(string repositoryRoot, string modulesRoot)
    {
        XDocument targets = XDocument.Load(Path.Combine(repositoryRoot, "AutoGenerated.targets"));
        var entries = new List<EngineModuleCatalogEntry>();
        foreach (XElement reference in targets.Descendants().Where(element => element.Name.LocalName == "PluginReference"))
        {
            string? include = (string?)reference.Attribute("Include");
            if (string.IsNullOrWhiteSpace(include))
                continue;
            string normalized = include.Replace("\\", "/", StringComparison.Ordinal);
            EngineModuleSide? side = normalized.Contains("Modules/Shared/", StringComparison.Ordinal) ? EngineModuleSide.Shared
                : normalized.Contains("Modules/Client/", StringComparison.Ordinal) ? EngineModuleSide.Client
                : normalized.Contains("Modules/Server/", StringComparison.Ordinal) ? EngineModuleSide.Server
                : null;
            string moduleId = Path.GetFileNameWithoutExtension(normalized);
            if (side is not null && Directory.Exists(Path.Combine(modulesRoot, moduleId)))
                entries.Add(new EngineModuleCatalogEntry(moduleId, side.Value));
        }
        Assert.Equal(
            Directory.EnumerateDirectories(modulesRoot).Select(Path.GetFileName).Order(StringComparer.Ordinal),
            entries.Select(entry => entry.ModuleId).Order(StringComparer.Ordinal));
        File.WriteAllText(
            Path.Combine(modulesRoot, EngineModuleCatalog.FileName),
            EngineModuleCatalog.Serialize(entries),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    private static void SeedOfflinePackageFeed(string repositoryRoot, string destination)
    {
        Directory.CreateDirectory(destination);
        string[] assetsPaths =
        [
            Path.Combine(repositoryRoot, "Karpik.Engine.Sdk.IntegrationTests", "obj", "project.assets.json"),
            Path.Combine(repositoryRoot, "Karpik.Engine.Sdk.Tasks", "obj", "project.assets.json"),
            Path.Combine(repositoryRoot, "Karpik.Engine.Sdk", "obj", "project.assets.json")
        ];
        foreach (string assetsPath in assetsPaths)
        {
            SeedOfflinePackageFeedFromAssets(assetsPath, destination);
        }
        Assert.NotEmpty(Directory.EnumerateFiles(destination, "*.nupkg", SearchOption.TopDirectoryOnly));
    }

    private static void SeedOfflinePackageFeedFromAssets(string assetsPath, string destination)
    {
        Assert.True(File.Exists(assetsPath), $"Project assets are missing: {assetsPath}");

        using JsonDocument assets = JsonDocument.Parse(File.ReadAllText(assetsPath));
        string[] packageFolders = assets.RootElement.GetProperty("packageFolders")
            .EnumerateObject()
            .Select(folder => Path.GetFullPath(folder.Name))
            .ToArray();
        Assert.NotEmpty(packageFolders);
        foreach (JsonProperty library in assets.RootElement.GetProperty("libraries").EnumerateObject())
        {
            if (!string.Equals(
                    library.Value.GetProperty("type").GetString(),
                    "package",
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string[] identity = library.Name.Split('/', 2);
            Assert.Equal(2, identity.Length);
            string? package = packageFolders
                .Select(folder => Path.Combine(
                    folder,
                    identity[0].ToLowerInvariant(),
                    identity[1].ToLowerInvariant()))
                .Where(Directory.Exists)
                .SelectMany(directory => Directory.EnumerateFiles(
                    directory,
                    "*.nupkg",
                    SearchOption.TopDirectoryOnly))
                .FirstOrDefault();
            Assert.True(package is not null,
                $"Resolved package {library.Name} has no .nupkg in project.assets.json packageFolders: " +
                string.Join(", ", packageFolders));
            File.Copy(package, Path.Combine(destination, Path.GetFileName(package)), overwrite: true);
        }
    }

    private static void WriteNuGetConfig(string gameRoot, string packageFeed, string offlinePackageFeed)
    {
        string escapedFeed = System.Security.SecurityElement.Escape(Path.GetFullPath(packageFeed))!;
        string escapedOfflinePackageFeed =
            System.Security.SecurityElement.Escape(Path.GetFullPath(offlinePackageFeed))!;
        string content = $$"""
            <?xml version="1.0" encoding="utf-8"?>
            <configuration>
              <config>
                <add key="globalPackagesFolder" value=".packages" />
              </config>
              <packageSources>
                <clear />
                <add key="Karpik local" value="{{escapedFeed}}" />
                <add key="Offline package cache" value="{{escapedOfflinePackageFeed}}" />
              </packageSources>
              <packageSourceMapping>
                <packageSource key="Karpik local">
                  <package pattern="Karpik.Engine.Sdk" />
                </packageSource>
                <packageSource key="Offline package cache">
                  <package pattern="*" />
                </packageSource>
              </packageSourceMapping>
            </configuration>
            """;
        File.WriteAllText(Path.Combine(gameRoot, "NuGet.Config"), content, new UTF8Encoding(false));
    }

    private async Task<ProcessResult> RunAsync(
        string workingDirectory,
        IReadOnlyList<string> arguments,
        IReadOnlyDictionary<string, string?> environment)
    {
        var startInfo = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }
        foreach ((string key, string? value) in environment)
        {
            startInfo.Environment[key] = value;
        }
        _startedProcesses.Add(new StartedProcess(
            arguments.ToArray(),
            new Dictionary<string, string?>(environment, StringComparer.OrdinalIgnoreCase)));

        using var process = new Process { StartInfo = startInfo };
        if (!process.Start())
        {
            throw new InvalidOperationException("Failed to start dotnet.");
        }
        Task<string> standardOutput = process.StandardOutput.ReadToEndAsync();
        Task<string> standardError = process.StandardError.ReadToEndAsync();
        try
        {
            Task exit = process.WaitForExitAsync();
            if (await Task.WhenAny(exit, Task.Delay(CommandTimeout)) != exit)
            {
                throw new TimeoutException($"dotnet {string.Join(' ', arguments)} exceeded {CommandTimeout}.");
            }
            await exit;
        }
        finally
        {
            await ExternalProcessTermination.EnsureStoppedAsync(
                new SystemExternalChildProcess(process),
                TerminationTimeout);
        }

        Task allOutput = Task.WhenAll(standardOutput, standardError);
        if (await Task.WhenAny(allOutput, Task.Delay(TerminationTimeout)) != allOutput)
        {
            throw ExternalProcessTermination.PreservationFailure(
                process.Id,
                $"Output pipes did not close within {TerminationTimeout} after process exit.",
                new TimeoutException("Redirected output drain did not complete after process exit."));
        }
        try
        {
            await allOutput;
        }
        catch (Exception exception) when (!ExternalProcessTermination.IsFatal(exception))
        {
            throw ExternalProcessTermination.PreservationFailure(
                process.Id,
                "Redirected output drain failed after process exit.",
                exception);
        }
        string output = standardOutput.Result;
        string error = standardError.Result;
        var result = new ProcessResult(process.ExitCode, output, error);
        _output.WriteLine($"> dotnet {string.Join(' ', arguments)}\n{result.CombinedOutput}");
        return result;
    }

    private void AssertAllSubprocessesUseOwnedState(string temporaryRoot)
    {
        Assert.NotEmpty(_startedProcesses);
        StartedProcess pack = Assert.Single(
            _startedProcesses,
            process => process.Arguments.FirstOrDefault() == "pack");
        string restoreConfigArgument = Assert.Single(
            pack.Arguments,
            argument => argument.StartsWith("-p:RestoreConfigFile=", StringComparison.OrdinalIgnoreCase));
        string restoreConfig = restoreConfigArgument[(restoreConfigArgument.IndexOf('=') + 1)..];
        Assert.True(IsWithinRoot(restoreConfig, temporaryRoot),
            $"dotnet pack received non-owned RestoreConfigFile: {restoreConfig}");
        Assert.True(File.Exists(restoreConfig), $"dotnet pack RestoreConfigFile is missing: {restoreConfig}");
        StartedProcess[] runnerBuildProcesses = _startedProcesses
            .Where(process => process.Arguments.Any(argument =>
                argument.EndsWith("Karpik.Engine.Core.Runner.csproj", StringComparison.OrdinalIgnoreCase)))
            .ToArray();
        Assert.Equal(2, runnerBuildProcesses.Length);
        foreach (StartedProcess process in runnerBuildProcesses)
        {
            foreach (string property in new[] { "RestoreConfigFile", "ArtifactsPath", "MSBuildProjectExtensionsPath" })
            {
                string prefix = $"-p:{property}=";
                string argument = Assert.Single(process.Arguments,
                    value => value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
                string value = argument[prefix.Length..];
                Assert.True(IsWithinRoot(value, temporaryRoot),
                    $"Runner build received non-owned {property}: {value}");
            }
        }
        foreach (StartedProcess process in _startedProcesses)
        {
            foreach (string key in new[] { "DOTNET_CLI_HOME", "NUGET_PACKAGES", "NUGET_HTTP_CACHE_PATH" })
            {
                Assert.True(process.Environment.TryGetValue(key, out string? value),
                    $"dotnet {string.Join(' ', process.Arguments)} did not receive {key}.");
                Assert.False(string.IsNullOrWhiteSpace(value));
                Assert.True(IsWithinRoot(value, temporaryRoot),
                    $"dotnet {string.Join(' ', process.Arguments)} received non-owned {key}: {value}");
            }
        }
    }

    private static void AssertSuccess(ProcessResult result, string operation) =>
        Assert.True(result.ExitCode == 0, $"Failed to {operation}.{Environment.NewLine}{result.CombinedOutput}");

    private static string ReadTopLevelProperty(XElement root, string propertyName) =>
        Assert.Single(root.Elements("PropertyGroup").Elements(propertyName)).Value.Trim();

    private static string GetTemplateRoot() => Path.Combine(GetRepositoryRoot(), "templates", "Karpik.Game");

    private static IEnumerable<string> EnumerateTemplateSourceFiles(string templateRoot) =>
        Directory.EnumerateFiles(templateRoot, "*", SearchOption.AllDirectories)
            .Where(path => !Path.GetRelativePath(templateRoot, path)
                .Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                .Any(part => part is "bin" or "obj"));

    private static string GetRepositoryRoot() =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));

    private static string Normalize(string path) => path.Replace('/', Path.DirectorySeparatorChar);

    private static StringComparison PathComparison =>
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    private static bool IsWithinRoot(string candidate, string root)
    {
        string fullCandidate = Path.GetFullPath(candidate);
        string fullRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        return fullCandidate.Equals(fullRoot, PathComparison) ||
               fullCandidate.StartsWith(fullRoot + Path.DirectorySeparatorChar, PathComparison);
    }

    private static void CopyDirectory(string source, string destination)
    {
        foreach (string directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
        {
            Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, directory)));
        }
        Directory.CreateDirectory(destination);
        foreach (string file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            string target = Path.Combine(destination, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }
    }

    private static Dictionary<string, FileStamp> SnapshotFiles(params string[] roots)
    {
        var snapshot = new Dictionary<string, FileStamp>(StringComparer.OrdinalIgnoreCase);
        foreach (string root in roots)
        {
            if (!Directory.Exists(root))
            {
                continue;
            }
            string rootName = Path.GetFileName(Path.TrimEndingDirectorySeparator(root));
            foreach (string file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
                         .Order(StringComparer.Ordinal))
            {
                var info = new FileInfo(file);
                using FileStream stream = File.OpenRead(file);
                string key = Path.Combine(rootName, Path.GetRelativePath(root, file));
                snapshot.Add(key, new FileStamp(
                    info.Length,
                    info.LastWriteTimeUtc.Ticks,
                    Convert.ToHexString(SHA256.HashData(stream))));
            }
        }
        return snapshot;
    }

    private static void DeleteOwnedTemporaryRoot(string root)
    {
        string fullRoot = Path.GetFullPath(root);
        string tempRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath()));
        if (!IsWithinRoot(fullRoot, tempRoot) || fullRoot.Equals(tempRoot, PathComparison))
        {
            throw new InvalidOperationException($"Refusing to delete a directory outside the temporary root: {fullRoot}");
        }
        if (Directory.Exists(fullRoot))
        {
            Directory.Delete(fullRoot, recursive: true);
        }
    }

    private sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError)
    {
        public string CombinedOutput => StandardOutput + Environment.NewLine + StandardError;
    }

    private sealed record StartedProcess(
        IReadOnlyList<string> Arguments,
        IReadOnlyDictionary<string, string?> Environment);

    private sealed record FileStamp(long Length, long LastWriteUtcTicks, string Sha256);

}
