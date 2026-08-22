using System.Collections.Concurrent;
using System.Diagnostics;
using System.Xml.Linq;
using Karpik.Engine.Core;
using Xunit;
using static Karpik.Engine.Sdk.IntegrationTests.ExternalGameCliTests;

namespace Karpik.Engine.Sdk.IntegrationTests;

public sealed class StaticCompositionCliTests
{
    private static readonly TimeSpan RunPhaseTimeout = TimeSpan.FromSeconds(60);
    private readonly ITestOutputHelper _output;

    public StaticCompositionCliTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void Template_launchers_wire_conditional_static_runtime_references()
    {
        string templateRoot = GetTemplateRoot();
        var expected = new Dictionary<string, (string Side, string RuntimeReference)>(StringComparer.Ordinal)
        {
            ["Source/KarpikGame.Client.Launcher/KarpikGame.Client.Launcher.csproj"] =
                ("Client", "..\\KarpikGame.Client\\KarpikGame.Client.csproj"),
            ["Source/KarpikGame.Server.Launcher/KarpikGame.Server.Launcher.csproj"] =
                ("Server", "..\\KarpikGame.Server\\KarpikGame.Server.csproj")
        };

        foreach ((string relativePath, (string side, string runtimeReference)) in expected)
        {
            string projectPath = Path.Combine(templateRoot, Normalize(relativePath));
            XDocument document = XDocument.Load(projectPath);
            XElement root = Assert.IsType<XElement>(document.Root);

            XElement[] projectReferences = root.Elements("ItemGroup").Elements("ProjectReference").ToArray();
            Assert.Equal(2, projectReferences.Length);
            XElement reference = Assert.Single(projectReferences, item =>
                (string?)item.Attribute("Include") == runtimeReference);
            Assert.Equal(runtimeReference, (string?)reference.Attribute("Include"));
            Assert.Null(reference.Attribute("Condition"));
            Assert.Equal("false", (string?)reference.Attribute("ReferenceOutputAssembly"));
            Assert.Equal("false", (string?)reference.Attribute("Private"));
            XElement sharedReference = Assert.Single(projectReferences, item =>
                ((string?)item.Attribute("Include") ?? string.Empty).EndsWith("KarpikGame.Shared.csproj", StringComparison.Ordinal));
            Assert.Null(sharedReference.Attribute("Condition"));
            Assert.Equal("false", (string?)sharedReference.Attribute("ReferenceOutputAssembly"));
            Assert.Equal("false", (string?)sharedReference.Attribute("Private"));

            XElement launcherCompile = Assert.Single(
                root.Elements("ItemGroup").Elements("Compile"),
                item => ((string?)item.Attribute("Include") ?? string.Empty).EndsWith("EngineLauncher.cs", StringComparison.Ordinal));
            Assert.Equal("'$(KarpikCompositionMode)' != 'Static'", (string?)launcherCompile.Attribute("Condition"));

            XElement staticEntryCompile = Assert.Single(
                root.Elements("ItemGroup").Elements("Compile"),
                item => (string?)item.Attribute("Include") == "StaticEntry.cs");
            Assert.Equal("'$(KarpikCompositionMode)' == 'Static'", (string?)staticEntryCompile.Attribute("Condition"));
            Assert.Equal("false",
                ReadTopLevelProperty(root, "EnableDefaultCompileItems"));
            XElement programCompile = Assert.Single(
                root.Elements("ItemGroup").Elements("Compile"),
                item => (string?)item.Attribute("Include") == "Program.cs");
            Assert.Equal("'$(KarpikCompositionMode)' != 'Static'", (string?)programCompile.Attribute("Condition"));

            XElement staticTarget = Assert.Single(root.Elements("Target"), target =>
                (string?)target.Attribute("Name") == "_KarpikEnableStaticRuntimeReference");
            Assert.Equal("'$(KarpikCompositionMode)' == 'Static'", (string?)staticTarget.Attribute("Condition"));
            Assert.Equal("AssignProjectConfiguration", (string?)staticTarget.Attribute("BeforeTargets"));

            XElement metadataItemGroup = Assert.Single(staticTarget.Elements("ItemGroup"));
            XElement flippedReference = Assert.Single(metadataItemGroup.Elements("ProjectReference"));
            Assert.Null(flippedReference.Attribute("Include"));
            Assert.Equal("true", (string?)flippedReference.Element("ReferenceOutputAssembly"));
            Assert.Equal("true", (string?)flippedReference.Element("Private"));

            // dotnet new strips C# preprocessor directives from template sources, so the
            // Static entry is a dedicated file selected by conditional Compile items.
            string staticEntry = File.ReadAllText(Path.Combine(
                templateRoot,
                Normalize($"Source/KarpikGame.{side}.Launcher/StaticEntry.cs")));
            Assert.DoesNotContain("#if", staticEntry, StringComparison.Ordinal);
            Assert.Contains($"StaticEngineHost.RunAsync(", staticEntry, StringComparison.Ordinal);
            Assert.Contains("new GeneratedRuntimeComposition()", staticEntry, StringComparison.Ordinal);
            Assert.Contains($"Side.{side}", staticEntry, StringComparison.Ordinal);

            string program = File.ReadAllText(Path.Combine(
                templateRoot,
                Normalize($"Source/KarpikGame.{side}.Launcher/Program.cs")));
            Assert.DoesNotContain("#if", program, StringComparison.Ordinal);
            Assert.Contains("EngineLauncher.RunAsync(", program, StringComparison.Ordinal);
            Assert.Contains(side, program, StringComparison.Ordinal);
            Assert.Contains($"KarpikGame.{side}", program, StringComparison.Ordinal);
        }
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Static_server_host_runs_without_plugin_context_or_module_manifest()
    {
        Assert.SkipUnless(
            Environment.GetEnvironmentVariable("KARPIK_RUN_EXTERNAL_SDK_INTEGRATION") == "1",
            "Set KARPIK_RUN_EXTERNAL_SDK_INTEGRATION=1 to run the external SDK subprocess suite.");

        string repositoryRoot = GetRepositoryRoot();
        string temporaryRoot = Path.Combine(Path.GetTempPath(), $"KarpikStaticHost_{Guid.NewGuid():N}");
        Directory.CreateDirectory(temporaryRoot);
        try
        {
            ExternalGameCliTests harness = new(_output);
            string retainedEngineRoot = ResolveRetainedEngineRoot(repositoryRoot);

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
                ["RestoreDisableParallel"] = "true"
            };

            // Restore every server-graph module with default package settings and
            // transaction-owned intermediates so its project.assets.json can seed the
            // offline feed without writing into repository Modules/**/obj.
            List<string> catalogModuleProjects = GetCatalogModuleProjects(repositoryRoot)
                .Where(path =>
                    path.Contains($"{Path.DirectorySeparatorChar}Modules{Path.DirectorySeparatorChar}Shared{Path.DirectorySeparatorChar}", StringComparison.Ordinal) ||
                    path.Contains($"{Path.DirectorySeparatorChar}Modules{Path.DirectorySeparatorChar}Server{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
                .ToList();
            string moduleSeedAssetsRoot = await harness.SeedModuleRestoresIntoOwnedStateAsync(
                repositoryRoot, catalogModuleProjects, temporaryRoot);

            string packageFeed = Path.Combine(temporaryRoot, "package-feed");
            Directory.CreateDirectory(packageFeed);
            string offlinePackageFeed = Path.Combine(temporaryRoot, "offline-packages");
            SeedOfflinePackageFeed(repositoryRoot, offlinePackageFeed, [moduleSeedAssetsRoot]);
            string hive = Path.Combine(temporaryRoot, "template-hive");
            string nugetConfig = Path.Combine(temporaryRoot, "NuGet.Config");
            WriteNuGetConfig(temporaryRoot, packageFeed, offlinePackageFeed);
            environment["KarpikEngineRoot"] = retainedEngineRoot;
            // The static composition generator can only discover current-shape module
            // installers, so every module of the launched side's graph must be rebuilt
            // from current sources. Client-only modules are out of the Server graph.
            string engineRoot = await harness.CreateUpdatedEngineInstallationAsync(
                repositoryRoot, retainedEngineRoot, temporaryRoot, nugetConfig, environment,
                catalogModuleProjects);
            environment.Remove("KarpikEngineRoot");
            environment["KarpikLocalApplicationDataRoot"] = Path.Combine(temporaryRoot, "local");

            await harness.PackSdkIntoOwnedFeedAsync(
                repositoryRoot, temporaryRoot, nugetConfig, packageFeed, environment);
            ProcessResult install = await harness.RunAsync(
                temporaryRoot,
                ["new", "--debug:custom-hive", hive, "install", GetTemplateRoot()],
                environment);
            AssertSuccess(install, "install the Karpik game template into an isolated hive");

            string gameName = "StaticHostGame";
            string gameRoot = Path.Combine(temporaryRoot, "game", gameName);
            await harness.MaterializeAsync(temporaryRoot, hive, gameRoot, gameName, environment);
            WriteNuGetConfig(gameRoot, packageFeed, offlinePackageFeed);

            string sharedProject = Path.Combine(
                gameRoot, "Source", $"{gameName}.Shared", $"{gameName}.Shared.csproj");
            string serverProject = Path.Combine(
                gameRoot, "Source", $"{gameName}.Server", $"{gameName}.Server.csproj");
            string serverLauncherProject = Path.Combine(
                gameRoot, "Source", $"{gameName}.Server.Launcher", $"{gameName}.Server.Launcher.csproj");
            SetCompositionMode(sharedProject, "Static", engineRoot);
            SetCompositionMode(serverProject, "Static", engineRoot);
            SetCompositionMode(serverLauncherProject, "Static", engineRoot);

            ProcessResult restore = await harness.RunAsync(
                gameRoot,
                ["restore", serverLauncherProject, "-m:1", "-nr:false"],
                environment);
            AssertSuccess(restore, "restore the Static Server host graph");
            ProcessResult build = await harness.RunAsync(
                gameRoot,
                ["build", serverLauncherProject, "-m:1", "-nr:false", "--no-restore"],
                environment);
            AssertSuccess(build, "build the Static Server host executable");

            await RunAndObserveStaticHostAsync(
                harness, environment, gameName, gameRoot, engineRoot);
            AssertNoOrphanHostProcesses(gameName);
            harness.AssertAllSubprocessesUseOwnedState(temporaryRoot);
        }
        finally
        {
            DeleteOwnedTemporaryRoot(temporaryRoot);
        }
    }

    private async Task RunAndObserveStaticHostAsync(
        ExternalGameCliTests harness,
        IReadOnlyDictionary<string, string?> environment,
        string gameName,
        string gameRoot,
        string engineRoot)
    {
        string launcherExecutable = Path.Combine(
            gameRoot,
            "Source",
            $"{gameName}.Server.Launcher",
            "bin",
            "Debug",
            "net10.0",
            OperatingSystem.IsWindows() ? $"{gameName}.Server.Launcher.exe" : gameName + ".Server.Launcher");
        string bundle = Path.Combine(
            gameRoot, "Source", $"{gameName}.Server", "bin", "Debug", "net10.0", "karpik-bundle");
        Assert.True(File.Exists(launcherExecutable), $"Static host executable is missing: {launcherExecutable}");
        Assert.True(Directory.Exists(bundle), $"Server runtime bundle is missing: {bundle}");

        var output = new ConcurrentQueue<string>();
        using var controller = new EditorPreviewController(
            new RuntimeLaunchOptions(Side.Server, launcherExecutable, bundle, engineRoot));
        controller.OutputReceived += output.Enqueue;

        // Phase 1: baseline generation. The host runs the unmutated game assembly.
        using (var timeout = new CancellationTokenSource(RunPhaseTimeout))
        {
            try
            {
                await controller.StartAsync(timeout.Token);
            }
            catch (Exception exception)
            {
                throw new InvalidOperationException(
                    $"Static Server host did not reach worker-ready.{Environment.NewLine}" +
                    string.Join(Environment.NewLine, output),
                    exception);
            }

            EditorRuntimeSnapshot? baselineSnapshot;
            try
            {
                Assert.Equal(EditorPreviewState.Running, controller.State);
                baselineSnapshot = await controller.RequestSnapshotAsync(
                    TimeSpan.FromSeconds(5), timeout.Token);
                Assert.NotNull(baselineSnapshot);
                Assert.True(baselineSnapshot.TotalEntityCount > 0,
                    $"Expected a non-empty ECS world in the Static host.{Environment.NewLine}" +
                    string.Join(Environment.NewLine, output));
                Assert.Contains(output,
                    line => line.Contains("[ServerGame] Created entity", StringComparison.Ordinal));
                Assert.Contains(output,
                    line => line.Contains("[ServerGame] Content: server runtime content", StringComparison.Ordinal));

                AssertDiagnosticTraceIsFreeOfDynamicLoading(output);
                AssertAllSubprocessArgumentsAreOwned(controller, output);

                // Clean shutdown releases the host executable so the transaction can
                // rebuild it with a mutated game assembly.
                await controller.StopAsync(timeout.Token);
                Assert.Equal(EditorPreviewState.Stopped, controller.State);
            }
            finally
            {
                if (controller.State != EditorPreviewState.Stopped)
                {
                    try { await controller.StopAsync(CancellationToken.None); } catch { }
                }
            }
        }

        // Phase 2: mutate the game assembly inside the transaction-owned materialized
        // game copy and rebuild the Static host from it. The worker is launched
        // directly from its build output (ProcessManager.StartWorkerCoreAsync), so the
        // rebuild must happen while no host process locks the executable.
        string serverProjectDirectory = Path.Combine(gameRoot, "Source", $"{gameName}.Server");
        string serverSystemSource = Path.Combine(serverProjectDirectory, "ServerGameInstaller.cs");
        const string mutationMarker = "[ServerGame] Reloaded game assembly marker";
        PatchStaticGameSource(serverSystemSource, mutationMarker);
        ProcessResult rebuild = await harness.RunAsync(
            gameRoot,
            ["build", Path.Combine(
                gameRoot, "Source", $"{gameName}.Server.Launcher", $"{gameName}.Server.Launcher.csproj"),
                "-m:1", "-nr:false"],
            environment);
        ExternalGameCliTests.AssertSuccess(rebuild, "rebuild the mutated Static Server host executable");

        // Phase 3: launch the rebuilt host and drive an IPC restart through it so
        // IRestartWorkerStateProvider state must survive across the mutated binary.
        using (var timeout = new CancellationTokenSource(RunPhaseTimeout))
        {
            try
            {
                await controller.StartAsync(timeout.Token);
            }
            catch (Exception exception)
            {
                throw new InvalidOperationException(
                    $"Rebuilt Static Server host did not reach worker-ready.{Environment.NewLine}" +
                    string.Join(Environment.NewLine, output),
                    exception);
            }

            try
            {
                Assert.Equal(EditorPreviewState.Running, controller.State);
                Assert.Contains(output,
                    line => line.Contains(mutationMarker, StringComparison.Ordinal));

                int rebuiltProcessId = Assert.IsType<int>(controller.ProcessId);
                EditorRuntimeSnapshot? rebuiltSnapshot = await controller.RequestSnapshotAsync(
                    TimeSpan.FromSeconds(5), timeout.Token);
                Assert.NotNull(rebuiltSnapshot);
                Assert.True(rebuiltSnapshot.TotalEntityCount > 0,
                    $"Expected a non-empty ECS world in the rebuilt Static host.{Environment.NewLine}" +
                    string.Join(Environment.NewLine, output));

                await controller.HotReloadAsync(timeout.Token);
                Assert.Equal(EditorPreviewState.Running, controller.State);
                int reloadedProcessId = Assert.IsType<int>(controller.ProcessId);
                Assert.NotEqual(rebuiltProcessId, reloadedProcessId);

                string stateRoot = Path.Combine(bundle, "reload", "state");
                Assert.True(!Directory.Exists(stateRoot) || !Directory.EnumerateFileSystemEntries(stateRoot).Any(),
                    "Hot reload state must be consumed by the restarted Static worker.");
                Assert.Contains(output,
                    line => line.Contains("Total modules with state: 1", StringComparison.Ordinal));

                EditorRuntimeSnapshot? snapshotAfterReload = await controller.RequestSnapshotAsync(
                    TimeSpan.FromSeconds(5), timeout.Token);
                Assert.NotNull(snapshotAfterReload);
                Assert.Equal(rebuiltSnapshot.TotalEntityCount + 1, snapshotAfterReload.TotalEntityCount);

                await controller.StopAsync(timeout.Token);
                Assert.Equal(EditorPreviewState.Stopped, controller.State);
                Assert.Null(controller.ProcessId);
                Assert.DoesNotContain(output,
                    line => line.Contains("Engine crashed", StringComparison.OrdinalIgnoreCase));
                string shadowRoot = Path.Combine(bundle, "reload", "shadow");
                Assert.True(!Directory.Exists(shadowRoot) || !Directory.EnumerateFileSystemEntries(shadowRoot).Any(),
                    "Clean stop must not leave shadow directories behind for a Static host run.");
            }
            finally
            {
                if (controller.State != EditorPreviewState.Stopped)
                {
                    try { await controller.StopAsync(CancellationToken.None); } catch { }
                }
            }
        }

        AssertDiagnosticTraceIsFreeOfDynamicLoading(output);
    }

    private static void PatchStaticGameSource(string serverSystemSourcePath, string mutationMarker)
    {
        string source = File.ReadAllText(serverSystemSourcePath);
        const string anchor =
            "[ServerGame] Created entity {entity} with GameComponent(42). Total entities: {world.Count}\");";
        Assert.True(source.Contains(anchor, StringComparison.Ordinal),
            $"Mutation anchor was not found in the materialized game source: {serverSystemSourcePath}");
        string mutated = source.Replace(
            anchor,
            anchor + Environment.NewLine +
            $"        Console.WriteLine(\"{mutationMarker}: rebuilt static host\");",
            StringComparison.Ordinal);
        Assert.NotEqual(source, mutated);
        File.WriteAllText(serverSystemSourcePath, mutated);
    }

    private void AssertDiagnosticTraceIsFreeOfDynamicLoading(ConcurrentQueue<string> output)
    {
        string trace = string.Join(Environment.NewLine, output);
        foreach (string forbidden in new[] { "PluginLoadContext", "modules.list", "shadow" })
        {
            Assert.True(!trace.Contains(forbidden, StringComparison.OrdinalIgnoreCase),
                $"Static host diagnostic trace must not contain '{forbidden}':{Environment.NewLine}{trace}");
        }
        Assert.True(!trace.Contains("Activator.CreateInstance", StringComparison.OrdinalIgnoreCase) &&
                    !trace.Contains("assembly.GetTypes()", StringComparison.OrdinalIgnoreCase),
            $"Static host diagnostic trace must not contain reflection-activation evidence:{Environment.NewLine}{trace}");
    }

    private void AssertAllSubprocessArgumentsAreOwned(EditorPreviewController controller, ConcurrentQueue<string> output)
    {
        Assert.Contains(output, line => line.Contains("[Worker] Starting...", StringComparison.Ordinal));
        Assert.Contains(output, line => line.Contains("[StaticHost]", StringComparison.Ordinal));
    }

    private void AssertNoOrphanHostProcesses(string gameName)
    {
        string[] forbiddenNames =
        [
            gameName + ".Server.Launcher",
            "Karpik.Engine.Core.Runner"
        ];
        foreach (string name in forbiddenNames)
        {
            Process[] orphans = Process.GetProcessesByName(name);
            foreach (Process orphan in orphans)
            {
                try
                {
                    orphan.Kill(entireProcessTree: true);
                    throw new InvalidOperationException(
                        $"Orphan process '{name}' (pid {orphan.Id}) survived the Static host test.");
                }
                finally
                {
                    orphan.Dispose();
                }
            }
        }
    }

    private static string GetTemplateRoot() =>
        Path.Combine(GetRepositoryRoot(), "templates", "Karpik.Game");

    private static string ReadTopLevelProperty(XElement root, string propertyName) =>
        Assert.Single(root.Elements("PropertyGroup").Elements(propertyName)).Value.Trim();

    private static string GetRepositoryRoot() =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));

    private static string Normalize(string path) => path.Replace('/', Path.DirectorySeparatorChar);
}
