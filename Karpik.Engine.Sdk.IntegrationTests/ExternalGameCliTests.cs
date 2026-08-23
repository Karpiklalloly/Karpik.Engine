using System.Collections.Concurrent;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
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

            XElement[] projectReferences = root.Elements("ItemGroup").Elements("ProjectReference").ToArray();
            Assert.Equal(2, projectReferences.Length);
            XElement reference = Assert.Single(projectReferences, item =>
                (string?)item.Attribute("Include") == runtimeReference);
            Assert.Equal(runtimeReference, (string?)reference.Attribute("Include"));
            Assert.Equal("false", (string?)reference.Attribute("ReferenceOutputAssembly"));
            Assert.Equal("false", (string?)reference.Attribute("Private"));
            XElement sharedReference = Assert.Single(projectReferences, item =>
                ((string?)item.Attribute("Include") ?? string.Empty).EndsWith(".Shared.csproj", StringComparison.Ordinal));
            Assert.Equal("false", (string?)sharedReference.Attribute("ReferenceOutputAssembly"));
            Assert.Equal("false", (string?)sharedReference.Attribute("Private"));
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
            string packageFeed = Path.Combine(temporaryRoot, "package-feed");
            Directory.CreateDirectory(packageFeed);
            string offlinePackageFeed = Path.Combine(temporaryRoot, "offline-packages");
            // Restore every catalog module with transaction-owned intermediates so its
            // project.assets.json can seed the offline feed without touching any
            // repository Modules/**/bin|obj state.
            string moduleSeedAssetsRoot = await SeedModuleRestoresIntoOwnedStateAsync(
                repositoryRoot,
                GetCatalogModuleProjects(repositoryRoot),
                temporaryRoot);
            SeedOfflinePackageFeed(repositoryRoot, offlinePackageFeed, [moduleSeedAssetsRoot]);
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
                commonEnvironment,
                // Stale payloads are incompatible with the current Core contracts;
                // every catalog module must be rebuilt from current sources.
                GetCatalogModuleProjects(repositoryRoot));
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
                    "--no-restore", $"-p:PackageVersion={PackageVersion}", $"-p:RestoreConfigFile={nugetConfig}", "-o", packageFeed],
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
                engineRoot,
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
                engineRoot,
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

    [Fact]
    [Trait("Category", "Integration")]
    public async Task External_static_composition_references_are_side_safe_and_dynamic_remains_unchanged()
    {
        Assert.SkipUnless(
            Environment.GetEnvironmentVariable("KARPIK_RUN_EXTERNAL_SDK_INTEGRATION") == "1",
            "Set KARPIK_RUN_EXTERNAL_SDK_INTEGRATION=1 to run the external SDK subprocess suite.");

        _startedProcesses.Clear();
        string repositoryRoot = GetRepositoryRoot();
        string temporaryRoot = Path.Combine(Path.GetTempPath(), $"KarpikStaticSdk_{Guid.NewGuid():N}");
        Directory.CreateDirectory(temporaryRoot);
        try
        {
            string retainedEngineRoot = ResolveRetainedEngineRoot(repositoryRoot);
            string packageFeed = Path.Combine(temporaryRoot, "package-feed");
            Directory.CreateDirectory(packageFeed);
            string offlinePackageFeed = Path.Combine(temporaryRoot, "offline-packages");
            // The engine transaction below resolves module-owned packages from the
            // offline feed only; seed it through transaction-owned module restores
            // so no repository Modules/**/bin|obj state is touched.
            List<string> sideGraphModuleProjects = GetCatalogModuleProjects(repositoryRoot).Where(path =>
                path.Contains($"{Path.DirectorySeparatorChar}Modules{Path.DirectorySeparatorChar}Shared{Path.DirectorySeparatorChar}", StringComparison.Ordinal) ||
                path.Contains($"{Path.DirectorySeparatorChar}Modules{Path.DirectorySeparatorChar}Server{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
                .ToList();
            string moduleSeedAssetsRoot = await SeedModuleRestoresIntoOwnedStateAsync(
                repositoryRoot, sideGraphModuleProjects, temporaryRoot);
            SeedOfflinePackageFeed(repositoryRoot, offlinePackageFeed, [moduleSeedAssetsRoot]);
            string hive = Path.Combine(temporaryRoot, "template-hive");
            string nugetConfig = Path.Combine(temporaryRoot, "NuGet.Config");
            WriteNuGetConfig(temporaryRoot, packageFeed, offlinePackageFeed);
            var environment = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
            {
                ["DOTNET_CLI_HOME"] = Path.Combine(temporaryRoot, "dotnet-home"),
                ["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1",
                ["DOTNET_NOLOGO"] = "1",
                ["DOTNET_CLI_USE_MSBUILD_SERVER"] = "0",
                ["MSBUILDDISABLENODEREUSE"] = "1",
                ["NUGET_PACKAGES"] = Path.Combine(temporaryRoot, "nuget-packages"),
                ["NUGET_HTTP_CACHE_PATH"] = Path.Combine(temporaryRoot, "nuget-http-cache"),
                ["NuGetAudit"] = "false",
                ["RestoreDisableParallel"] = "true",
                ["KarpikEngineRoot"] = retainedEngineRoot
            };
            string engineRoot = await CreateUpdatedEngineInstallationAsync(
                repositoryRoot, retainedEngineRoot, temporaryRoot, nugetConfig, environment,
                // Fresh payloads keep every module directory internally consistent:
                // referencing one fresh assembly makes RAR resolve its dependencies
                // from the same directory, so stale siblings must not survive.
                sideGraphModuleProjects);
            environment.Remove("KarpikEngineRoot");
            environment["KarpikLocalApplicationDataRoot"] = Path.Combine(temporaryRoot, "local");

            await PackSdkIntoOwnedFeedAsync(
                repositoryRoot, temporaryRoot, nugetConfig, packageFeed, environment);
            ProcessResult install = await RunAsync(
                temporaryRoot,
                ["new", "--debug:custom-hive", hive, "install", GetTemplateRoot()],
                environment);
            AssertSuccess(install, "install the Karpik game template into an isolated hive");

            string staticRoot = Path.Combine(temporaryRoot, "static", "StaticGame");
            await MaterializeAsync(temporaryRoot, hive, staticRoot, "StaticGame", environment);
            WriteNuGetConfig(staticRoot, packageFeed, offlinePackageFeed);
            ProcessResult staticRestore = await RunAsync(staticRoot, ["restore", "StaticGame.slnx", "-m:1", "-nr:false"], environment);
            AssertSuccess(staticRestore, "restore the Static positive fixture");
            await AssertStaticSharedProjectCompilesAgainstInstalledModulesAsync(staticRoot, "StaticGame", engineRoot, environment);

            string dynamicRoot = Path.Combine(temporaryRoot, "dynamic", "DynamicGame");
            await MaterializeAsync(temporaryRoot, hive, dynamicRoot, "DynamicGame", environment);
            WriteNuGetConfig(dynamicRoot, packageFeed, offlinePackageFeed);
            ProcessResult dynamicRestore = await RunAsync(dynamicRoot, ["restore", "DynamicGame.slnx", "-m:1", "-nr:false"], environment);
            AssertSuccess(dynamicRestore, "restore the Dynamic fixture");
            await AssertDynamicSharedProjectDoesNotReceiveStaticModuleReferencesAsync(dynamicRoot, "DynamicGame", engineRoot, environment);
            await AssertStaticSideBoundariesAsync(temporaryRoot, hive, packageFeed, offlinePackageFeed, environment);
            AssertAllSubprocessesUseOwnedState(temporaryRoot);
        }
        finally
        {
            DeleteOwnedTemporaryRoot(temporaryRoot);
        }
    }

    internal async Task MaterializeAsync(
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
        string engineRoot,
        IReadOnlyDictionary<string, string?> environment)
    {
        string project = Path.Combine(gameRoot, "Source", $"{gameName}.Shared", $"{gameName}.Shared.csproj");
        string diagnostics = Path.Combine(Path.GetDirectoryName(project)!, "static-reference-diagnostics.txt");
        string catalog = Path.Combine(engineRoot, "modules", EngineModuleCatalog.FileName);
        string spatialAssembly = Path.Combine(engineRoot, "modules", "Spatial2D", "Spatial2D.dll");
        SetCompositionMode(project, "Static", engineRoot);
        XDocument document = XDocument.Load(project, LoadOptions.PreserveWhitespace);
        document.Root!.Add(
            new XElement("Target",
                new XAttribute("Name", "CaptureStaticModuleReferences"),
                new XAttribute("AfterTargets", "_KarpikResolveStaticModuleReferences"),
                new XAttribute("BeforeTargets", "ResolveAssemblyReferences"),
                new XElement("WriteLinesToFile",
                    new XAttribute("File", diagnostics),
                    new XAttribute("Lines", "mode=$(KarpikCompositionMode);kind=$(KarpikProjectKind);side=$(KarpikSide);root=$(KarpikEngineRoot)"),
                    new XAttribute("Overwrite", "true")),
                new XElement("WriteLinesToFile",
                    new XAttribute("File", diagnostics),
                    new XAttribute("Lines", "@(_KarpikStaticModuleReference->'%(Identity)|%(AssemblyIdentity)')"),
                    new XAttribute("Overwrite", "false")),
                new XElement("WriteLinesToFile",
                    new XAttribute("File", diagnostics),
                    new XAttribute("Lines", "@(Reference->'%(Identity)|%(HintPath)')"),
                    new XAttribute("Overwrite", "false"))));
        document.Save(project);
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
        string captured = File.Exists(diagnostics) ? File.ReadAllText(diagnostics) : "<diagnostic target did not run>";
        string spatialIdentity = File.Exists(spatialAssembly)
            ? AssemblyName.GetAssemblyName(spatialAssembly).FullName ?? "<no full identity>"
            : "<missing>";
        Assert.True(build.ExitCode == 0,
            $"Failed to compile a Static Shared project against Karpik.Engine.Shared.Spatial2D.Transform2D without a manual reference.{Environment.NewLine}" +
            $"Catalog:{Environment.NewLine}{File.ReadAllText(catalog)}{Environment.NewLine}" +
            $"Spatial2D DLL: {spatialAssembly} ({spatialIdentity}){Environment.NewLine}" +
            $"MSBuild diagnostic:{Environment.NewLine}{captured}{Environment.NewLine}{build.CombinedOutput}");
    }

    private async Task AssertDynamicSharedProjectDoesNotReceiveStaticModuleReferencesAsync(
        string gameRoot,
        string gameName,
        string engineRoot,
        IReadOnlyDictionary<string, string?> environment)
    {
        string project = Path.Combine(gameRoot, "Source", $"{gameName}.Shared", $"{gameName}.Shared.csproj");
        string references = Path.Combine(Path.GetDirectoryName(project)!, "dynamic-references.txt");
        XDocument document = XDocument.Load(project, LoadOptions.PreserveWhitespace);
        document.Root!.Elements("PropertyGroup").First().Add(new XElement("KarpikEngineRoot", engineRoot));
        document.Root!.Add(
            new XElement("Target",
                new XAttribute("Name", "CaptureDynamicReferences"),
                new XAttribute("AfterTargets", "_KarpikResolveEngineReferenceAssemblies"),
                new XAttribute("BeforeTargets", "ResolveAssemblyReferences"),
                new XAttribute("DependsOnTargets", "_KarpikResolveEngineReferenceAssemblies"),
                new XElement("WriteLinesToFile",
                    new XAttribute("File", references),
                    new XAttribute("Lines", "@(Reference->'%(Identity)|%(HintPath)')"),
                    new XAttribute("Overwrite", "true"))));
        document.Save(project);
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

        string[] evaluatedReferences = File.ReadAllLines(references);
        Assert.NotEmpty(evaluatedReferences);
        string modulesRoot = Path.Combine(engineRoot, "modules");
        string networkSharedCore = Path.Combine(modulesRoot, "Network.Shared.Core", "Network.Shared.Core.dll");
        Assert.True(File.Exists(networkSharedCore), $"Network.Shared.Core contract assembly is missing: {networkSharedCore}");
        string projectDirectory = Path.GetDirectoryName(project)!;
        (string Identity, string HintPath)[] hintedReferences = evaluatedReferences
            .Select(reference => reference.Split('|', 2))
            .Where(parts => parts.Length == 2 && !string.IsNullOrWhiteSpace(parts[1]))
            .Select(parts => (
                Identity: parts[0],
                HintPath: Path.GetFullPath(parts[1], projectDirectory)))
            .ToArray();
        string runner = Path.GetFullPath(Path.Combine(engineRoot, "runners", "server"));
        string[] engineReferences = hintedReferences
            .Where(reference => IsWithinRoot(reference.HintPath, engineRoot))
            .Select(reference => $"{reference.Identity}|{reference.HintPath}")
            .Order(StringComparer.Ordinal)
            .ToArray();
        string[] expectedEngineReferences = new[]
        {
            $"Autofac|{Path.Combine(runner, "Autofac.dll")}",
            $"DragonECS|{Path.Combine(runner, "DragonECS.dll")}",
            $"Network.Shared.Core|{networkSharedCore}",
            $"Karpik.Engine.Core|{Path.Combine(runner, "Karpik.Engine.Core.dll")}",
            $"System.Composition.AttributedModel|{Path.Combine(runner, "System.Composition.AttributedModel.dll")}"
        }.Order(StringComparer.Ordinal).ToArray();
        Assert.True(
            expectedEngineReferences.SequenceEqual(engineReferences, StringComparer.Ordinal),
            $"Dynamic engine references changed.{Environment.NewLine}" +
            $"Expected:{Environment.NewLine}{string.Join(Environment.NewLine, expectedEngineReferences)}{Environment.NewLine}" +
            $"Actual engine-owned:{Environment.NewLine}{string.Join(Environment.NewLine, engineReferences)}{Environment.NewLine}" +
            $"Captured:{Environment.NewLine}{string.Join(Environment.NewLine, evaluatedReferences)}");
        // Dynamic projects must not receive module references beyond the shared
        // network contract that the generated snapshot registry requires.
        Assert.DoesNotContain(hintedReferences, reference =>
            IsWithinRoot(reference.HintPath, modulesRoot) &&
            !reference.HintPath.Equals(networkSharedCore, PathComparison));
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

    internal static void SetCompositionMode(string projectPath, string mode, string? engineRoot = null)
    {
        XDocument document = XDocument.Load(projectPath, LoadOptions.PreserveWhitespace);
        XElement root = Assert.IsType<XElement>(document.Root);
        XElement propertyGroup = root.Elements("PropertyGroup").First();
        propertyGroup.Add(new XElement("KarpikCompositionMode", mode));
        if (engineRoot is not null)
        {
            propertyGroup.Add(new XElement("KarpikEngineRoot", engineRoot));
        }
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

    /// <summary>
    /// Module projects declared by AutoGenerated.targets, filtered to the given
    /// side folders (Shared/Server/Client). The static composition generator can
    /// only discover current-shape installers, so transaction engines must carry
    /// freshly built payloads instead of the retained historical ones.
    /// </summary>
    internal static List<string> GetCatalogModuleProjects(string repositoryRoot)
    {
        XDocument targets = XDocument.Load(Path.Combine(repositoryRoot, "AutoGenerated.targets"));
        List<string> projects = targets.Descendants()
            .Where(element => element.Name.LocalName == "PluginReference")
            .Select(element => (string?)element.Attribute("Include"))
            .OfType<string>()
            .Select(include => include.Replace('/', Path.DirectorySeparatorChar))
            .Select(include =>
            {
                int marker = include.IndexOf("Modules" + Path.DirectorySeparatorChar, StringComparison.Ordinal);
                return marker < 0 ? include : include[marker..];
            })
            .Select(relative => Path.GetFullPath(Path.Combine(repositoryRoot, relative)))
            .Where(File.Exists)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList();
        Assert.NotEmpty(projects);
        return projects;
    }

    internal async Task<string> CreateUpdatedEngineInstallationAsync(
        string repositoryRoot,
        string retainedEngineRoot,
        string temporaryRoot,
        string nugetConfig,
        IReadOnlyDictionary<string, string?> environment,
        IReadOnlyList<string>? extraModuleProjects = null)
    {
        string runnerProject = Path.Combine(repositoryRoot, "Karpik.Engine.Core.Runner", "Karpik.Engine.Core.Runner.csproj");
        string spatialProject = Path.Combine(repositoryRoot, "Modules", "Shared", "Spatial2D", "Spatial2D.csproj");
        string networkCoreProject = Path.Combine(
            repositoryRoot,
            "Modules",
            "Shared",
            "Network.Shared",
            "Network.Shared.Core",
            "Network.Shared.Core.csproj");
        string ownedBuildRoot = Path.Combine(temporaryRoot, "runner-build");
        string artifacts = Path.Combine(ownedBuildRoot, "artifacts") + Path.DirectorySeparatorChar;
        string props = Path.Combine(ownedBuildRoot, "Directory.Build.props");
        Directory.CreateDirectory(ownedBuildRoot);
        File.WriteAllText(props, $$"""
            <Project>
              <Import Project="{{Path.Combine(repositoryRoot, "Directory.Build.props")}}" />
              <PropertyGroup>
                <BaseIntermediateOutputPath>{{Path.Combine(ownedBuildRoot, "obj")}}{{Path.DirectorySeparatorChar}}$(MSBuildProjectName){{Path.DirectorySeparatorChar}}</BaseIntermediateOutputPath>
                <MSBuildProjectExtensionsPath>$(BaseIntermediateOutputPath)</MSBuildProjectExtensionsPath>
              </PropertyGroup>
            </Project>
            """);
        Dictionary<string, FileStamp> runnerBefore = SnapshotFiles(
            Path.Combine(repositoryRoot, "Karpik.Engine.Core.Runner", "bin"),
            Path.Combine(repositoryRoot, "Karpik.Engine.Core.Runner", "obj"));
        Dictionary<string, FileStamp> spatialBefore = SnapshotFiles(
            Path.Combine(repositoryRoot, "Modules", "Shared", "Spatial2D", "bin"),
            Path.Combine(repositoryRoot, "Modules", "Shared", "Spatial2D", "obj"));
        Dictionary<string, FileStamp> networkCoreBefore = SnapshotFiles(
            Path.Combine(repositoryRoot, "Modules", "Shared", "Network.Shared", "Network.Shared.Core", "bin"),
            Path.Combine(repositoryRoot, "Modules", "Shared", "Network.Shared", "Network.Shared.Core", "obj"));
        string[] ownedProperties =
        [
            $"-p:RestoreConfigFile={nugetConfig}",
            $"-p:ArtifactsPath={artifacts}",
            $"-p:DirectoryBuildPropsPath={props}"
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
        ProcessResult spatialRestore = await RunAsync(
            repositoryRoot,
            ["restore", spatialProject, "-m:1", "-nr:false", .. ownedProperties],
            environment);
        AssertSuccess(spatialRestore, "restore the current Spatial2D module into transaction-owned state");
        ProcessResult spatialBuild = await RunAsync(
            repositoryRoot,
            ["build", spatialProject, "-c", "Release", "-m:1", "-nr:false", "--no-restore", .. ownedProperties],
            environment);
        AssertSuccess(spatialBuild, "build the current Spatial2D module into transaction-owned state");
        ProcessResult networkCoreRestore = await RunAsync(
            repositoryRoot,
            ["restore", networkCoreProject, "-m:1", "-nr:false", .. ownedProperties],
            environment);
        AssertSuccess(networkCoreRestore, "restore the current Network.Shared.Core module into transaction-owned state");
        ProcessResult networkCoreBuild = await RunAsync(
            repositoryRoot,
            ["build", networkCoreProject, "-c", "Release", "-m:1", "-nr:false", "--no-restore", .. ownedProperties],
            environment);
        AssertSuccess(networkCoreBuild, "build the current Network.Shared.Core module into transaction-owned state");
        Assert.Equal(runnerBefore, SnapshotFiles(
            Path.Combine(repositoryRoot, "Karpik.Engine.Core.Runner", "bin"),
            Path.Combine(repositoryRoot, "Karpik.Engine.Core.Runner", "obj")));
        Assert.Equal(spatialBefore, SnapshotFiles(
            Path.Combine(repositoryRoot, "Modules", "Shared", "Spatial2D", "bin"),
            Path.Combine(repositoryRoot, "Modules", "Shared", "Spatial2D", "obj")));
        Assert.Equal(networkCoreBefore, SnapshotFiles(
            Path.Combine(repositoryRoot, "Modules", "Shared", "Network.Shared", "Network.Shared.Core", "bin"),
            Path.Combine(repositoryRoot, "Modules", "Shared", "Network.Shared", "Network.Shared.Core", "obj")));

        string prepared = Path.Combine(temporaryRoot, "prepared-task5-engine");
        foreach (string directory in new[] { "editor", "sdk", "modules", "native" })
        {
            CopyDirectory(Path.Combine(retainedEngineRoot, directory), Path.Combine(prepared, directory));
        }
        string spatialOutput = Path.Combine(artifacts, "bin", "Spatial2D", "release");
        Assert.True(File.Exists(Path.Combine(spatialOutput, "Spatial2D.dll")),
            $"Spatial2D transaction output is missing: {spatialOutput}");
        Assert.True(IsWithinRoot(spatialOutput, ownedBuildRoot));
        CopySpatial2DModulePayload(spatialOutput, Path.Combine(prepared, "modules", "Spatial2D"));
        string networkCoreOutput = Path.Combine(artifacts, "bin", "Network.Shared.Core", "release");
        string networkCoreAssembly = Path.Combine(networkCoreOutput, "Network.Shared.Core.dll");
        Assert.True(File.Exists(networkCoreAssembly),
            $"Network.Shared.Core transaction output is missing: {networkCoreOutput}");
        Assert.True(IsWithinRoot(networkCoreOutput, ownedBuildRoot));
        string[] installedNetworkCoreCopies = Directory.GetFiles(
            Path.Combine(prepared, "modules"),
            "Network.Shared.Core.dll",
            SearchOption.AllDirectories);
        Assert.NotEmpty(installedNetworkCoreCopies);
        foreach (string installedNetworkCoreCopy in installedNetworkCoreCopies)
        {
            File.Copy(networkCoreAssembly, installedNetworkCoreCopy, overwrite: true);
        }
        if (extraModuleProjects is { Count: > 0 })
        {
            await BuildExtraModulesIntoPreparedPayloadAsync(
                repositoryRoot,
                extraModuleProjects,
                Path.Combine(prepared, "modules"),
                nugetConfig,
                ownedBuildRoot,
                environment);
        }
        WriteEngineModuleCatalog(repositoryRoot, Path.Combine(prepared, "modules"));
        string runnerOutput = Path.Combine(artifacts, "bin", "Karpik.Engine.Core.Runner", "release");
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

    /// <summary>
    /// Restores repository module projects with transaction-owned intermediates
    /// (<c>-p:ArtifactsPath</c> under <paramref name="temporaryRoot"/>), mirroring
    /// <see cref="BuildExtraModulesIntoPreparedPayloadAsync"/>, so the resulting
    /// project.assets.json files can seed the offline feed while repository
    /// Modules/**/bin|obj stay untouched. Default package feeds remain in effect
    /// on purpose: the seed must harvest exactly what a plain developer restore
    /// resolves, so no NuGet cache/feed environment is redirected.
    /// Returns the owned root holding the seeded project.assets.json files.
    /// </summary>
    internal async Task<string> SeedModuleRestoresIntoOwnedStateAsync(
        string repositoryRoot,
        IReadOnlyList<string> moduleProjects,
        string temporaryRoot)
    {
        string seedBuildRoot = Path.Combine(temporaryRoot, "module-seed-build");
        string artifacts = Path.Combine(seedBuildRoot, "artifacts") + Path.DirectorySeparatorChar;
        Dictionary<string, FileStamp>[] repositoriesBefore = new Dictionary<string, FileStamp>[moduleProjects.Count];
        for (int index = 0; index < moduleProjects.Count; index++)
        {
            string moduleProject = moduleProjects[index];
            Assert.True(File.Exists(moduleProject), $"Module project is missing: {moduleProject}");
            string projectDirectory = Path.GetDirectoryName(moduleProject)!;
            repositoriesBefore[index] = SnapshotFiles(
                Path.Combine(projectDirectory, "bin"),
                Path.Combine(projectDirectory, "obj"));
            ProcessResult seedRestore = await RunAsync(
                repositoryRoot,
                ["restore", moduleProject, "-m:1", "-nr:false", $"-p:ArtifactsPath={artifacts}"],
                new Dictionary<string, string?>());
            AssertSuccess(seedRestore, $"restore {Path.GetFileName(moduleProject)} into transaction-owned seeding state");
        }
        for (int index = 0; index < moduleProjects.Count; index++)
        {
            string projectDirectory = Path.GetDirectoryName(moduleProjects[index])!;
            Assert.Equal(repositoriesBefore[index], SnapshotFiles(
                Path.Combine(projectDirectory, "bin"),
                Path.Combine(projectDirectory, "obj")));
        }
        return Path.Combine(seedBuildRoot, "artifacts", "obj");
    }

    /// <summary>
    /// Rebuilds engine module projects into transaction-owned outputs and replaces the
    /// corresponding prepared module payloads with fresh binaries. Needed when the
    /// retained installation predates current module contracts (for example static
    /// composition discovery requires current IModuleInstaller shapes).
    /// </summary>
    private async Task BuildExtraModulesIntoPreparedPayloadAsync(
        string repositoryRoot,
        IReadOnlyList<string> moduleProjects,
        string preparedModulesRoot,
        string nugetConfig,
        string ownedBuildRoot,
        IReadOnlyDictionary<string, string?> environment)
    {
        string artifacts = Path.Combine(ownedBuildRoot, "artifacts") + Path.DirectorySeparatorChar;
        string[] ownedProperties =
        [
            $"-p:RestoreConfigFile={nugetConfig}",
            $"-p:ArtifactsPath={artifacts}",
            $"-p:DirectoryBuildPropsPath={Path.Combine(ownedBuildRoot, "Directory.Build.props")}"
        ];
        foreach (string project in moduleProjects)
        {
            Assert.True(File.Exists(project), $"Module project is missing: {project}");
            ProcessResult restore = await RunAsync(
                repositoryRoot,
                ["restore", project, "-m:1", "-nr:false", .. ownedProperties],
                environment);
            AssertSuccess(restore, $"restore {Path.GetFileName(project)} into transaction-owned state");
            ProcessResult build = await RunAsync(
                repositoryRoot,
                ["build", project, "-c", "Release", "-m:1", "-nr:false", "--no-restore", .. ownedProperties],
                environment);
            AssertSuccess(build, $"build {Path.GetFileName(project)} into transaction-owned outputs");

            string moduleId = Path.GetFileNameWithoutExtension(project);
            string output = Path.Combine(artifacts, "bin", moduleId, "release");
            Assert.True(IsWithinRoot(output, ownedBuildRoot), $"Module output escaped the transaction: {output}");
            ReplaceModulePayload(output, Path.Combine(preparedModulesRoot, moduleId), moduleId);
            PropagatePayloadAssemblies(Path.Combine(preparedModulesRoot, moduleId), preparedModulesRoot);
        }
    }

    /// <summary>
    /// Overwrites every same-named assembly copy across all module payloads so the
    /// staged engine never holds byte-distinct assemblies with one identity.
    /// </summary>
    private static void PropagatePayloadAssemblies(string freshModuleDirectory, string preparedModulesRoot)
    {
        foreach (string freshAssembly in Directory.EnumerateFiles(freshModuleDirectory, "*.dll", SearchOption.TopDirectoryOnly))
        {
            string fileName = Path.GetFileName(freshAssembly);
            foreach (string staleCopy in Directory.EnumerateFiles(
                preparedModulesRoot, fileName, SearchOption.AllDirectories))
            {
                if (!string.Equals(Path.GetFullPath(staleCopy), Path.GetFullPath(freshAssembly), StringComparison.OrdinalIgnoreCase))
                {
                    File.Copy(freshAssembly, staleCopy, overwrite: true);
                }
            }
        }
    }

    private static void ReplaceModulePayload(string sourceDirectory, string destinationDirectory, string moduleId)
    {
        Assert.True(File.Exists(Path.Combine(sourceDirectory, moduleId + ".dll")),
            $"Fresh module payload is missing its primary assembly: {sourceDirectory}");
        if (Directory.Exists(destinationDirectory))
        {
            Directory.Delete(destinationDirectory, recursive: true);
        }
        Directory.CreateDirectory(destinationDirectory);
        foreach (string file in Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.TopDirectoryOnly))
        {
            string fileName = Path.GetFileName(file);
            if (fileName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                || fileName.StartsWith("Karpik.Engine.Core.Runner", StringComparison.OrdinalIgnoreCase)
                || fileName.EndsWith(".pdb", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            if (!fileName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
                && !fileName.Equals(moduleId + ".deps.json", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            File.Copy(file, Path.Combine(destinationDirectory, fileName));
        }

        // Package-native payloads must keep their runtimes/<rid>/native layout:
        // the dynamic PluginLoadContext probes exactly these subdirectories, and
        // NeoVeldrid.SPIRV (shaderc/spirv-cross) fails its type initializer
        // without them.
        foreach (string nativeRootName in new[] { "runtimes", "native" })
        {
            string nativeRoot = Path.Combine(sourceDirectory, nativeRootName);
            if (!Directory.Exists(nativeRoot))
            {
                continue;
            }
            foreach (string file in Directory.EnumerateFiles(nativeRoot, "*", SearchOption.AllDirectories))
            {
                string destination = Path.Combine(
                    destinationDirectory,
                    Path.GetRelativePath(sourceDirectory, file));
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.Copy(file, destination);
            }
        }
    }

    internal async Task PackSdkIntoOwnedFeedAsync(
        string repositoryRoot,
        string temporaryRoot,
        string nugetConfig,
        string packageFeed,
        IReadOnlyDictionary<string, string?> environment)
    {
        string sdkProject = Path.Combine(repositoryRoot, "Karpik.Engine.Sdk", "Karpik.Engine.Sdk.csproj");
        string codegenProject = Path.Combine(
            repositoryRoot,
            "Karpik.Engine.Core.Generator",
            "Karpik.Engine.Core.Codegen",
            "Karpik.Engine.Core.Codegen.csproj");
        string ownedBuildRoot = Path.Combine(temporaryRoot, "sdk-build");
        string artifacts = Path.Combine(ownedBuildRoot, "artifacts") + Path.DirectorySeparatorChar;
        string props = Path.Combine(ownedBuildRoot, "Directory.Build.props");
        Directory.CreateDirectory(ownedBuildRoot);
        File.WriteAllText(props, $$"""
            <Project>
              <Import Project="{{Path.Combine(repositoryRoot, "Directory.Build.props")}}" />
              <PropertyGroup>
                <BaseIntermediateOutputPath>{{Path.Combine(ownedBuildRoot, "obj")}}{{Path.DirectorySeparatorChar}}$(MSBuildProjectName){{Path.DirectorySeparatorChar}}</BaseIntermediateOutputPath>
                <MSBuildProjectExtensionsPath>$(BaseIntermediateOutputPath)</MSBuildProjectExtensionsPath>
              </PropertyGroup>
            </Project>
            """);

        Dictionary<string, FileStamp> repositoryOutputsBefore = SnapshotSdkRepositoryBuildOutputs(repositoryRoot);
        string[] ownedProperties =
        [
            $"-p:RestoreConfigFile={nugetConfig}",
            $"-p:ArtifactsPath={artifacts}",
            $"-p:DirectoryBuildPropsPath={props}"
        ];
        ProcessResult sdkRestore = await RunAsync(
            repositoryRoot,
            ["restore", sdkProject, "-m:1", "-nr:false", .. ownedProperties],
            environment);
        AssertSuccess(sdkRestore, "restore the SDK and its task project into transaction-owned state");
        ProcessResult sdkBuild = await RunAsync(
            repositoryRoot,
            ["build", sdkProject, "-c", "Debug", "-m:1", "-nr:false", "--no-restore", .. ownedProperties],
            environment);
        AssertSuccess(sdkBuild, "build the SDK and its task project into transaction-owned outputs");
        ProcessResult codegenRestore = await RunAsync(
            repositoryRoot,
            ["restore", codegenProject, "-m:1", "-nr:false", .. ownedProperties],
            environment);
        AssertSuccess(codegenRestore, "restore Karpik.Engine.Core.Codegen into transaction-owned state");
        ProcessResult codegenBuild = await RunAsync(
            repositoryRoot,
            ["build", codegenProject, "-c", "Debug", "-m:1", "-nr:false", "--no-restore", .. ownedProperties],
            environment);
        AssertSuccess(codegenBuild, "build Karpik.Engine.Core.Codegen into transaction-owned outputs");

        string tasksOutput = Path.Combine(artifacts, "bin", "Karpik.Engine.Sdk.Tasks", "debug") +
                             Path.DirectorySeparatorChar;
        foreach (string fileName in new[]
                 {
                     "Karpik.Engine.Sdk.Tasks.dll",
                     "Karpik.Engine.ProjectModel.dll",
                     "Karpik.Engine.Tooling.dll"
                 })
        {
            string output = Path.Combine(tasksOutput, fileName);
            Assert.True(File.Exists(output), $"Transaction-owned SDK task output is missing: {output}");
        }
        string codegenOutput = Path.Combine(artifacts, "bin", "Karpik.Engine.Core.Codegen", "debug") +
                               Path.DirectorySeparatorChar;
        string networkCodegenOutput = Path.Combine(artifacts, "bin", "Network.Codegen", "debug") +
                                      Path.DirectorySeparatorChar;
        Assert.True(File.Exists(Path.Combine(codegenOutput, "Karpik.Engine.Core.Codegen.dll")),
            $"Transaction-owned codegen output is missing: {codegenOutput}");
        Assert.True(File.Exists(Path.Combine(networkCodegenOutput, "Network.Codegen.dll")),
            $"Transaction-owned network codegen output is missing: {networkCodegenOutput}");
        Assert.True(IsWithinRoot(tasksOutput, ownedBuildRoot));
        Assert.True(IsWithinRoot(codegenOutput, ownedBuildRoot));
        Assert.True(IsWithinRoot(networkCodegenOutput, ownedBuildRoot));

        ProcessResult pack = await RunAsync(
            repositoryRoot,
            [
                "pack", sdkProject, "-c", "Debug", "-m:1", "-nr:false", "--no-build", "--no-restore",
                $"-p:PackageVersion={PackageVersion}",
                $"-p:KarpikSdkTasksOutputPath={tasksOutput}",
                $"-p:KarpikCoreCodegenOutputPath={codegenOutput}",
                $"-p:KarpikNetworkCodegenOutputPath={networkCodegenOutput}",
                .. ownedProperties,
                "-o", packageFeed
            ],
            environment);
        AssertSuccess(pack, "pack the current Karpik.Engine.Sdk package from transaction-owned outputs");
        Assert.True(File.Exists(Path.Combine(packageFeed, $"Karpik.Engine.Sdk.{PackageVersion}.nupkg")));
        Assert.True(IsWithinRoot(packageFeed, temporaryRoot));
        Assert.Equal(repositoryOutputsBefore, SnapshotSdkRepositoryBuildOutputs(repositoryRoot));
    }

    private static void CopySpatial2DModulePayload(string sourceDirectory, string destinationDirectory)
    {
        Directory.CreateDirectory(destinationDirectory);
        foreach (string fileName in new[] { "Spatial2D.dll", "OpenTK.Mathematics.dll" })
        {
            string source = Path.Combine(sourceDirectory, fileName);
            Assert.True(File.Exists(source), $"Spatial2D module payload is missing required assembly: {source}");
            File.Copy(source, Path.Combine(destinationDirectory, fileName), overwrite: true);
        }
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
        catch (Exception exception)
        {
            throw new InvalidOperationException(
                $"Multi-worker ECS cycle failed.{Environment.NewLine}" +
                $"--- server ---{Environment.NewLine}{string.Join(Environment.NewLine, serverOutput)}{Environment.NewLine}" +
                $"--- client1 ---{Environment.NewLine}{string.Join(Environment.NewLine, client1Output)}{Environment.NewLine}" +
                $"--- client2 ---{Environment.NewLine}{string.Join(Environment.NewLine, client2Output)}",
                exception);
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

    internal static string ResolveRetainedEngineRoot(string repositoryRoot)
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

    internal static void WriteEngineModuleCatalog(string repositoryRoot, string modulesRoot)
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

    internal static void SeedOfflinePackageFeed(
        string repositoryRoot,
        string destination,
        IReadOnlyCollection<string>? additionalAssetRoots = null)
    {
        Directory.CreateDirectory(destination);
        string[] assetsPaths =
        [
            Path.Combine(repositoryRoot, "Karpik.Engine.Sdk.IntegrationTests", "obj", "project.assets.json"),
            Path.Combine(repositoryRoot, "Karpik.Engine.Sdk.Tasks", "obj", "project.assets.json"),
            Path.Combine(repositoryRoot, "Karpik.Engine.Sdk", "obj", "project.assets.json"),
            Path.Combine(repositoryRoot, "Karpik.Engine.Core.Generator", "Karpik.Engine.Core.Codegen", "obj", "project.assets.json"),
            Path.Combine(repositoryRoot, "Tools", "StaticAnalyzer", "obj", "project.assets.json")
        ];
        foreach (string assetsPath in assetsPaths)
        {
            SeedOfflinePackageFeedFromAssets(assetsPath, destination);
        }
        // Module-owned packages come from transaction-owned restore intermediates
        // (see SeedModuleRestoresIntoOwnedStateAsync); repository Modules/**/obj is
        // never read or written here.
        foreach (string additionalRoot in additionalAssetRoots ?? [])
        {
            Assert.True(Directory.Exists(additionalRoot),
                $"Transaction-owned module seeding root is missing: {additionalRoot}");
            foreach (string assetsPath in Directory.EnumerateFiles(
                additionalRoot, "project.assets.json", SearchOption.AllDirectories))
            {
                SeedOfflinePackageFeedFromAssets(assetsPath, destination);
            }
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

    internal static void WriteNuGetConfig(string gameRoot, string packageFeed, string offlinePackageFeed)
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

    internal async Task<ProcessResult> RunAsync(
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

    internal void AssertAllSubprocessesUseOwnedState(string temporaryRoot)
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
            foreach (string property in new[] { "RestoreConfigFile", "ArtifactsPath", "DirectoryBuildPropsPath" })
            {
                string prefix = $"-p:{property}=";
                string argument = Assert.Single(process.Arguments,
                    value => value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
                string value = argument[prefix.Length..];
                Assert.True(IsWithinRoot(value, temporaryRoot),
                    $"Runner build received non-owned {property}: {value}");
            }
        }
        string intermediateProps = Path.Combine(temporaryRoot, "runner-build", "Directory.Build.props");
        Assert.True(File.Exists(intermediateProps));
        Assert.Contains("$(MSBuildProjectName)", File.ReadAllText(intermediateProps), StringComparison.Ordinal);
        foreach (StartedProcess process in _startedProcesses)
        {
            if (IsDefaultFeedSeedRestore(process, out string? project))
            {
                // Seed restores deliberately keep the default package feeds and caches,
                // but their intermediates must stay inside the transaction.
                Assert.True(!IsWithinRoot(project!, temporaryRoot),
                    $"Seed restore project must live outside the transaction root: {project}");
                string prefix = "-p:ArtifactsPath=";
                string artifactsArgument = Assert.Single(process.Arguments,
                    value => value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
                string artifactsPath = artifactsArgument[prefix.Length..];
                Assert.True(IsWithinRoot(artifactsPath, temporaryRoot),
                    $"dotnet restore {project} seeded the offline feed with non-owned intermediates: {artifactsPath}");
                continue;
            }
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

    /// <summary>
    /// Offline-feed seed restores: a plain <c>restore</c> of an absolute module
    /// project path with default package settings and owned
    /// <c>-p:ArtifactsPath</c> intermediates instead of repository bin/obj.
    /// </summary>
    private static bool IsDefaultFeedSeedRestore(
        StartedProcess process,
        out string? restoredProject)
    {
        restoredProject = null;
        if (process.Arguments.FirstOrDefault() != "restore")
        {
            return false;
        }
        if (process.Arguments.Any(argument =>
                argument.StartsWith("-p:RestoreConfigFile=", StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }
        if (!process.Arguments.Any(argument =>
                argument.StartsWith("-p:ArtifactsPath=", StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }
        restoredProject = process.Arguments
            .Skip(1)
            .FirstOrDefault(argument => !argument.StartsWith("-", StringComparison.Ordinal));
        return !string.IsNullOrWhiteSpace(restoredProject);
    }

    internal static void AssertSuccess(ProcessResult result, string operation) =>
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

    internal static bool IsWithinRoot(string candidate, string root)
    {
        string fullCandidate = Path.GetFullPath(candidate);
        string fullRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        return fullCandidate.Equals(fullRoot, PathComparison) ||
               fullCandidate.StartsWith(fullRoot + Path.DirectorySeparatorChar, PathComparison);
    }

    internal static void CopyDirectory(string source, string destination)
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

    private static Dictionary<string, FileStamp> SnapshotSdkRepositoryBuildOutputs(string repositoryRoot)
    {
        string[] projectDirectories =
        [
            "Karpik.Engine.Sdk",
            "Karpik.Engine.Sdk.Tasks",
            "Karpik.Engine.ProjectModel",
            "Karpik.Engine.Tooling",
            Path.Combine("Karpik.Engine.Core.Generator", "Karpik.Engine.Core.Codegen")
        ];
        var snapshot = new Dictionary<string, FileStamp>(StringComparer.OrdinalIgnoreCase);
        foreach (string projectDirectory in projectDirectories)
        {
            string projectName = Path.GetFileName(projectDirectory);
            string[] roots =
            [
                Path.Combine(repositoryRoot, projectDirectory, "bin"),
                Path.Combine(repositoryRoot, projectDirectory, "obj"),
                Path.Combine(repositoryRoot, "artifacts", "bin", projectName),
                Path.Combine(repositoryRoot, "artifacts", "obj", projectName)
            ];
            foreach (string root in roots.Where(Directory.Exists))
            {
                foreach (string file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
                             .Order(StringComparer.Ordinal))
                {
                    var info = new FileInfo(file);
                    using FileStream stream = File.OpenRead(file);
                    string key = Path.GetRelativePath(repositoryRoot, file);
                    snapshot.Add(key, new FileStamp(
                        info.Length,
                        info.LastWriteTimeUtc.Ticks,
                        Convert.ToHexString(SHA256.HashData(stream))));
                }
            }
        }
        string repositoryPackageFeed = Path.Combine(repositoryRoot, "artifacts", "nuget");
        if (Directory.Exists(repositoryPackageFeed))
        {
            foreach (string file in Directory.EnumerateFiles(repositoryPackageFeed, "*", SearchOption.AllDirectories)
                         .Order(StringComparer.Ordinal))
            {
                var info = new FileInfo(file);
                using FileStream stream = File.OpenRead(file);
                string key = Path.GetRelativePath(repositoryRoot, file);
                snapshot.Add(key, new FileStamp(
                    info.Length,
                    info.LastWriteTimeUtc.Ticks,
                    Convert.ToHexString(SHA256.HashData(stream))));
            }
        }
        return snapshot;
    }

    internal static void DeleteOwnedTemporaryRoot(string root)
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

    internal sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError)
    {
        public string CombinedOutput => StandardOutput + Environment.NewLine + StandardError;
    }

    private sealed record StartedProcess(
        IReadOnlyList<string> Arguments,
        IReadOnlyDictionary<string, string?> Environment);

    private sealed record FileStamp(long Length, long LastWriteUtcTicks, string Sha256);

}
