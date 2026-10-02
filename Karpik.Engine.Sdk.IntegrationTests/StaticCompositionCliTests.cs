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

    // NativeAOT hosts start and restart measurably slower than JIT hosts, so the
    // gated AOT acceptance runs get a dedicated overall budget.
    private static readonly TimeSpan AotRunPhaseTimeout = TimeSpan.FromMinutes(10);
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
            StaticTemplateGame game = await PrepareStaticTemplateGameAsync(
                harness,
                repositoryRoot,
                temporaryRoot,
                path => path.Contains($"{Path.DirectorySeparatorChar}Modules{Path.DirectorySeparatorChar}Shared{Path.DirectorySeparatorChar}", StringComparison.Ordinal) ||
                        path.Contains($"{Path.DirectorySeparatorChar}Modules{Path.DirectorySeparatorChar}Server{Path.DirectorySeparatorChar}", StringComparison.Ordinal));

            ProcessResult restore = await harness.RunAsync(
                game.GameRoot,
                ["restore", game.ServerLauncherProject, "-m:1", "-nr:false"],
                game.Environment);
            AssertSuccess(restore, "restore the Static Server host graph");
            ProcessResult build = await harness.RunAsync(
                game.GameRoot,
                ["build", game.ServerLauncherProject, "-m:1", "-nr:false", "--no-restore"],
                game.Environment);
            AssertSuccess(build, "build the Static Server host executable");
            AssertCookedLauncherContent(Path.Combine(game.GameRoot, "Source", $"{game.GameName}.Server.Launcher", "bin", "Debug", "net10.0"));

            ProcessResult publish = await harness.RunAsync(
                game.GameRoot,
                ["publish", game.ServerLauncherProject, "-m:1", "-nr:false", "--no-restore"],
                game.Environment);
            AssertSuccess(publish, "publish the Static Server host with cooked content");
            AssertCookedLauncherContent(Path.Combine(game.GameRoot, "Source", $"{game.GameName}.Server.Launcher", "bin", "Release", "net10.0", "publish"));

            await RunAndObserveStaticHostAsync(
                harness, game.Environment, game.GameName, game.GameRoot, game.EngineRoot);
            AssertNoOrphanHostProcesses(game.GameName);
            harness.AssertAllSubprocessesUseOwnedState(temporaryRoot);
        }
        finally
        {
            if (Environment.GetEnvironmentVariable("KARPIK_KEEP_TEST_TEMP") == "1") { Console.WriteLine("KEEPING TEMP ROOT: " + temporaryRoot); } else { DeleteOwnedTemporaryRoot(temporaryRoot); }
        }
    }

    private static void AssertCookedLauncherContent(string output)
    {
        string content = Path.Combine(output, "Content");
        Assert.True(File.Exists(Path.Combine(content, "manifest.json")), $"Missing cooked manifest: {output}");
        Assert.NotEmpty(Directory.EnumerateFiles(Path.Combine(content, "artifacts"), "*.cooked", SearchOption.AllDirectories));
    }

    private static readonly string[] NativeAotOnlinePackagePatterns =
    [
        "Microsoft.DotNet.*",
        "Microsoft.NET.ILLink.Tasks",
        "Microsoft.NETCore.App.Runtime.*",
        "Microsoft.WindowsDesktop.App.Runtime.*",
        "Microsoft.AspNetCore.App.Runtime.*",
        "runtime.win-*"
    ];

    internal sealed record StaticTemplateGame(
        ExternalGameCliTests Harness,
        Dictionary<string, string?> Environment,
        string GameName,
        string GameRoot,
        string EngineRoot,
        string ServerLauncherProject,
        string ClientLauncherProject);

    /// <summary>
    /// Shared transaction-owned preparation for the gated Static host tests:
    /// owned module seeding, offline feed, rebuilt engine installation of the
    /// filtered side graph, SDK pack, template install and materialization with
    /// every project switched to KarpikCompositionMode=Static.
    /// </summary>
    private async Task<StaticTemplateGame> PrepareStaticTemplateGameAsync(
        ExternalGameCliTests harness,
        string repositoryRoot,
        string temporaryRoot,
        Func<string, bool> catalogModuleProjectFilter,
        bool allowNativeAotOnlineFeed = false)
    {
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

        // Restore every module of the exercised graph with default package settings
        // and transaction-owned intermediates so its project.assets.json can seed the
        // offline feed without writing into repository Modules/**/obj.
        List<string> catalogModuleProjects = GetCatalogModuleProjects(repositoryRoot)
            .Where(catalogModuleProjectFilter)
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
        environment["KarpikEngineRoot"] = ResolveRetainedEngineRoot(repositoryRoot);
        // The static composition generator can only discover current-shape module
        // installers, so every module of the exercised graphs must be rebuilt
        // from current sources inside the transaction.
        string engineRoot = await harness.CreateUpdatedEngineInstallationAsync(
            repositoryRoot, environment["KarpikEngineRoot"]!, temporaryRoot, nugetConfig, environment,
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
        WriteNuGetConfig(
            gameRoot,
            packageFeed,
            offlinePackageFeed,
            allowNativeAotOnlineFeed ? NativeAotOnlinePackagePatterns : null);

        string sharedProject = Path.Combine(
            gameRoot, "Source", $"{gameName}.Shared", $"{gameName}.Shared.csproj");
        string serverProject = Path.Combine(
            gameRoot, "Source", $"{gameName}.Server", $"{gameName}.Server.csproj");
        string clientProject = Path.Combine(
            gameRoot, "Source", $"{gameName}.Client", $"{gameName}.Client.csproj");
        string serverLauncherProject = Path.Combine(
            gameRoot, "Source", $"{gameName}.Server.Launcher", $"{gameName}.Server.Launcher.csproj");
        string clientLauncherProject = Path.Combine(
            gameRoot, "Source", $"{gameName}.Client.Launcher", $"{gameName}.Client.Launcher.csproj");
        SetCompositionMode(sharedProject, "Static", engineRoot);
        SetCompositionMode(serverProject, "Static", engineRoot);
        SetCompositionMode(clientProject, "Static", engineRoot);
        SetCompositionMode(serverLauncherProject, "Static", engineRoot);
        SetCompositionMode(clientLauncherProject, "Static", engineRoot);

        return new StaticTemplateGame(
            harness, environment, gameName, gameRoot, engineRoot,
            serverLauncherProject, clientLauncherProject);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Static_server_host_publishes_and_runs_under_NativeAot_with_ten_reload_cycles()
    {
        Assert.SkipUnless(
            Environment.GetEnvironmentVariable("KARPIK_RUN_EXTERNAL_SDK_INTEGRATION") == "1" &&
            Environment.GetEnvironmentVariable("KARPIK_RUN_AOT_ACCEPTANCE") == "1",
            "Set KARPIK_RUN_EXTERNAL_SDK_INTEGRATION=1 and KARPIK_RUN_AOT_ACCEPTANCE=1 to run the NativeAOT acceptance gates.");

        string repositoryRoot = GetRepositoryRoot();
        string temporaryRoot = Path.Combine(Path.GetTempPath(), $"KarpikStaticAot_{Guid.NewGuid():N}");
        Directory.CreateDirectory(temporaryRoot);
        try
        {
            StaticTemplateGame game = await PrepareStaticTemplateGameAsync(
                new(_output),
                repositoryRoot,
                temporaryRoot,
                path => path.Contains($"{Path.DirectorySeparatorChar}Modules{Path.DirectorySeparatorChar}", StringComparison.Ordinal),
                allowNativeAotOnlineFeed: true);

            // Gate 1: production-shaped Server publish (win-x64, AOT + trimming +
            // invariant globalization; the template prints no culture-formatted data).
            ProcessResult publish = await game.Harness.RunAsync(
                game.GameRoot,
                [
                    "publish", game.ServerLauncherProject, "-c", "Release", "-r", "win-x64",
                    "-p:PublishAot=true", "-p:InvariantGlobalization=true", "-p:TrimmerSingleWarn=false", "-m:1", "-nr:false"
                ],
                game.Environment,
                TimeSpan.FromMinutes(30));
            AssertSuccess(publish, "publish the Static Server host under NativeAOT");

            // The launcher csproj carries NO NoWarn: every trim/AOT warning reaches the
            // publish output and must classify exactly against the documented
            // (code -> originating assembly[/member]) inventory in
            // docs/02_ADR/static-runtime-composition.md. Anything outside it fails.
            AssertAotWarningsMatchDocumentedInventory(publish, ServerDocumentedAotWarnings);

            string gameName = game.GameName;
            string publishDirectory = Path.Combine(
                game.GameRoot, "Source", $"{gameName}.Server.Launcher",
                "bin", "Release", "net10.0", "win-x64", "publish");
            string executable = Path.Combine(publishDirectory, OperatingSystem.IsWindows()
                ? $"{gameName}.Server.Launcher.exe"
                : gameName + ".Server.Launcher");
            Assert.True(File.Exists(executable), $"Published Server host is missing: {executable}");

            // The published output must be manifest-free: no managed module staging.
            // Configuration/assets live in the static bundle validated below; the
            // headless Server graph carries no third-party native payloads.
            AssertDoesNotContainManagedManifest(publishDirectory);
            // The Release bundle built by the runtime project must be valid static layout.
            string bundle = FindReleaseStaticBundle(game.GameRoot, gameName, "Server", Side.Server);

            // Gate 1 run evidence: startup, fixed ticks, snapshot round-trip, clean shutdown.
            var output = new ConcurrentQueue<string>();
            using var controller = new EditorPreviewController(
                new RuntimeLaunchOptions(Side.Server, executable, bundle, game.EngineRoot));
            controller.OutputReceived += output.Enqueue;
            using (var timeout = new CancellationTokenSource(AotRunPhaseTimeout))
            {
                await StartOrThrowAsync(controller, timeout.Token, output, "published NativeAOT Server host");
                EditorRuntimeSnapshot? snapshot;
                try
                {
                    // AOT startup is slower than JIT: give the first snapshot a
                    // longer window before declaring the gate failed.
                    snapshot = await controller.RequestSnapshotAsync(TimeSpan.FromSeconds(20), timeout.Token);
                }
                catch (Exception exception)
                {
                    throw new InvalidOperationException(
                        $"Snapshot request against the published NativeAOT Server host failed.{Environment.NewLine}" +
                        string.Join(Environment.NewLine, output),
                        exception);
                }

                Assert.True(snapshot is not null,
                    $"Snapshot was null.{Environment.NewLine}{string.Join(Environment.NewLine, output)}");
                Assert.True(snapshot.TotalEntityCount > 0,
                    $"Expected a non-empty ECS world.{Environment.NewLine}{string.Join(Environment.NewLine, output)}");
                Assert.True(
                    snapshot.Entities.Any(entity =>
                        entity.Components.Any(component =>
                            component.TypeName.Contains("GameComponent", StringComparison.Ordinal) &&
                            component.DisplayValue.Contains("42", StringComparison.Ordinal))),
                    string.Join(Environment.NewLine, snapshot.Entities.Select(e =>
                        $"{e.EntityId}: [{string.Join("; ", e.Components.Select(c => $"{c.TypeName} = {c.DisplayValue}"))}]")) +
                        Environment.NewLine + string.Join(Environment.NewLine, output));

                // Gate 3: ten process-isolated reload cycles with state round-trip.
                // The template init system is idempotent on a restored world, so
                // after the warm-up cycle every restart must observe a stable world.
                int warmUpEntities = -1;
                foreach (int cycle in Enumerable.Range(1, 10))
                {
                    int beforeProcessId = Assert.IsType<int>(controller.ProcessId);
                    try
                    {
                        await controller.HotReloadAsync(timeout.Token);
                    }
                    catch (Exception exception)
                    {
                        throw new InvalidOperationException(
                            $"Reload cycle {cycle}: hot reload did not reach worker-ready.{Environment.NewLine}" +
                            string.Join(Environment.NewLine, output),
                            exception);
                    }
                    int afterProcessId = Assert.IsType<int>(controller.ProcessId);
                    Assert.NotEqual(beforeProcessId, afterProcessId);

                    string stateRoot = Path.Combine(bundle, "reload", "state");
                    Assert.True(!Directory.Exists(stateRoot) || !Directory.EnumerateFileSystemEntries(stateRoot).Any(),
                        $"Reload cycle {cycle}: state payload must be consumed by the restarted worker.{Environment.NewLine}" +
                        string.Join(Environment.NewLine, output));

                    EditorRuntimeSnapshot? reloaded;
                    try
                    {
                        reloaded = await controller.RequestSnapshotAsync(TimeSpan.FromSeconds(20), timeout.Token);
                    }
                    catch (Exception exception)
                    {
                        throw new InvalidOperationException(
                            $"Reload cycle {cycle}: snapshot request failed.{Environment.NewLine}" +
                            string.Join(Environment.NewLine, output),
                            exception);
                    }
                    Assert.NotNull(reloaded);
                    if (cycle == 1)
                    {
                        // Warm-up cycle: absorbs any first-restart state-shape
                        // effects; every later cycle must be byte-stable.
                        warmUpEntities = reloaded.TotalEntityCount;
                        continue;
                    }
                    Assert.True(reloaded.TotalEntityCount == warmUpEntities,
                        $"Reload cycle {cycle}: world must stay stable after the warm-up reload; expected {warmUpEntities} entities, got {reloaded.TotalEntityCount}.{Environment.NewLine}" +
                        string.Join(Environment.NewLine, output));
                }

                await controller.StopAsync(timeout.Token);
                Assert.Equal(EditorPreviewState.Stopped, controller.State);
                Assert.Null(controller.ProcessId);
            }

            Assert.DoesNotContain(output, line => line.Contains("Engine crashed", StringComparison.OrdinalIgnoreCase));
            int stateCollections = output.Count(line => line.Contains("Total modules with state:", StringComparison.Ordinal));
            Assert.True(stateCollections >= 10,
                $"Expected at least ten state round-trips, got {stateCollections}.{Environment.NewLine}{string.Join(Environment.NewLine, output)}");
            long[] collectedBytes = output
                .Where(line => line.Contains("Collected state from module", StringComparison.Ordinal))
                .Select(line =>
                {
                    int open = line.LastIndexOf('(');
                    int close = line.IndexOf(" bytes)", StringComparison.Ordinal);
                    return open > 0 && close > open ? long.Parse(line[(open + 1)..close]) : -1L;
                })
                .Where(size => size >= 0)
                .ToArray();
            Assert.True(collectedBytes.Length >= 10, "Expected at least ten collected-state reports.");
            // The template init system is idempotent on a restored world, so the
            // saved payload must be byte-stable after the first warm-up reload.
            // ANY sustained growth (linear included) is a leak and fails the gate;
            // only tiny digit-jitter around identical sizes is tolerated.
            long warmUpBytes = collectedBytes[0];
            for (int i = 1; i < collectedBytes.Length; i++)
            {
                Assert.True(Math.Abs(collectedBytes[i] - warmUpBytes) <= 16,
                    "Saved state payload must stay stable after the warm-up reload (no linear growth): sizes [" +
                    string.Join(", ", collectedBytes) + "]");
            }

            // No locked publish files and no orphan processes.
            using (FileStream unlocked = File.Open(executable, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
            AssertNoOrphanHostProcesses(gameName);
            game.Harness.AssertAllSubprocessesUseOwnedState(temporaryRoot);
        }
        finally
        {
            if (Environment.GetEnvironmentVariable("KARPIK_KEEP_TEST_TEMP") == "1") { Console.WriteLine("KEEPING TEMP ROOT: " + temporaryRoot); } else { DeleteOwnedTemporaryRoot(temporaryRoot); }
        }
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Static_client_host_publishes_and_runs_under_NativeAot()
    {
        Assert.SkipUnless(
            Environment.GetEnvironmentVariable("KARPIK_RUN_EXTERNAL_SDK_INTEGRATION") == "1" &&
            Environment.GetEnvironmentVariable("KARPIK_RUN_AOT_ACCEPTANCE") == "1",
            "Set KARPIK_RUN_EXTERNAL_SDK_INTEGRATION=1 and KARPIK_RUN_AOT_ACCEPTANCE=1 to run the NativeAOT acceptance gates.");

        string repositoryRoot = GetRepositoryRoot();
        string temporaryRoot = Path.Combine(Path.GetTempPath(), $"KarpikStaticAotClient_{Guid.NewGuid():N}");
        Directory.CreateDirectory(temporaryRoot);
        try
        {
            StaticTemplateGame game = await PrepareStaticTemplateGameAsync(
                new(_output),
                repositoryRoot,
                temporaryRoot,
                path => path.Contains($"{Path.DirectorySeparatorChar}Modules{Path.DirectorySeparatorChar}", StringComparison.Ordinal),
                allowNativeAotOnlineFeed: true);

            ProcessResult publish = await game.Harness.RunAsync(
                game.GameRoot,
                [
                    "publish", game.ClientLauncherProject, "-c", "Release", "-r", "win-x64",
                    "-p:PublishAot=true", "-p:InvariantGlobalization=true", "-p:TrimmerSingleWarn=false", "-m:1", "-nr:false"
                ],
                game.Environment,
                TimeSpan.FromMinutes(30));
            AssertSuccess(publish, "publish the Static Client host under NativeAOT");

            // Same exact-pairs inventory contract as the Server gate; client adds
            // Silk.NET single-file probing origins (see ADR warning inventory).
            AssertAotWarningsMatchDocumentedInventory(publish, ClientDocumentedAotWarnings);

            string gameName = game.GameName;
            string publishDirectory = Path.Combine(
                game.GameRoot, "Source", $"{gameName}.Client.Launcher",
                "bin", "Release", "net10.0", "win-x64", "publish");
            string executable = Path.Combine(publishDirectory, OperatingSystem.IsWindows()
                ? $"{gameName}.Client.Launcher.exe"
                : gameName + ".Client.Launcher");
            Assert.True(File.Exists(executable), $"Published Client host is missing: {executable}");
            AssertDoesNotContainManagedManifest(publishDirectory);

            string bundle = FindReleaseStaticBundle(game.GameRoot, gameName, "Client", Side.Client);

            // Gate 2 run evidence: window creation, graphics backend initialization,
            // one rendered frame, input initialization and clean shutdown. All four
            // subsystems resolve through the DI graph during startup, so any failure
            // surfaces as an activation/crash line instead of these markers.
            var output = new ConcurrentQueue<string>();
            using var controller = new EditorPreviewController(
                new RuntimeLaunchOptions(Side.Client, executable, bundle, game.EngineRoot));
            controller.OutputReceived += output.Enqueue;
            using (var timeout = new CancellationTokenSource(AotRunPhaseTimeout))
            {
                await StartOrThrowAsync(controller, timeout.Token, output, "published NativeAOT Client host");
                Assert.Contains(output,
                    line => line.Contains("[ClientGame] World has", StringComparison.Ordinal));
                await WaitForOutputLineAsync(output, "[ClientGame] First frame rendered.", timeout.Token);
                await controller.StopAsync(timeout.Token);
                Assert.Equal(EditorPreviewState.Stopped, controller.State);
                Assert.Null(controller.ProcessId);
            }

            Assert.DoesNotContain(output, line => line.Contains("Engine crashed", StringComparison.OrdinalIgnoreCase));
            AssertNoOrphanHostProcesses(gameName);
            game.Harness.AssertAllSubprocessesUseOwnedState(temporaryRoot);
        }
        finally
        {
            if (Environment.GetEnvironmentVariable("KARPIK_KEEP_TEST_TEMP") == "1") { Console.WriteLine("KEEPING TEMP ROOT: " + temporaryRoot); } else { DeleteOwnedTemporaryRoot(temporaryRoot); }
        }
    }

    private static string FindReleaseStaticBundle(string gameRoot, string gameName, string sideProject, Side side)
    {
        // With a global RID the inner runtime project output (and its bundle) may
        // live under bin/Release/net10.0/win-x64; without it, bin/Release/net10.0.
        string releaseRoot = Path.Combine(gameRoot, "Source", $"{gameName}.{sideProject}", "bin", "Release");
        string[] candidates = [Path.Combine(releaseRoot, "net10.0", "karpik-bundle")];
        candidates = [.. candidates,
            .. Directory.EnumerateDirectories(releaseRoot, "karpik-bundle", SearchOption.AllDirectories)];
        string bundle = candidates.FirstOrDefault(Directory.Exists)
            ?? throw new InvalidOperationException($"Release static bundle is missing under: {releaseRoot}");
        Assert.Equal(bundle, RuntimeBundleLayout.ValidateStatic(bundle, side));
        return bundle;
    }

    private static async Task StartOrThrowAsync(
        EditorPreviewController controller,
        CancellationToken token,
        ConcurrentQueue<string> output,
        string description)
    {
        try
        {
            await controller.StartAsync(token);
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException(
                $"{description} did not reach worker-ready.{Environment.NewLine}" +
                string.Join(Environment.NewLine, output),
                exception);
        }
    }

    private static async Task WaitForOutputLineAsync(
        ConcurrentQueue<string> output,
        string marker,
        CancellationToken token)
    {
        while (!output.Any(line => line.Contains(marker, StringComparison.Ordinal)))
        {
            token.ThrowIfCancellationRequested();
            await Task.Delay(100, token);
        }
    }

    private static void AssertDoesNotContainManagedManifest(string rootDirectory)
    {
        Assert.False(Directory.EnumerateFiles(rootDirectory, "modules.list", SearchOption.AllDirectories).Any(),
            $"Published output must not contain a managed module manifest: {rootDirectory}");
        Assert.False(Directory.EnumerateDirectories(rootDirectory, "modules.version.*", SearchOption.AllDirectories).Any(),
            $"Published output must not contain managed module staging: {rootDirectory}");
        Assert.DoesNotContain(
            Directory.EnumerateFileSystemEntries(rootDirectory, "*", SearchOption.AllDirectories),
            entry => Path.GetFileName(entry).Contains("shadow", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>A single trim/AOT warning classified by code and exact origin.</summary>
    internal sealed record AotWarning(string Code, string Origin);

    // Matches both aggregate form ("IL2104: Assembly 'X' produced ... warnings.")
    // and member form ("IL3000: 'Ns.Type.Member()' ...") of ilc/ILLink diagnostics.
    private static readonly System.Text.RegularExpressions.Regex AotWarningRegex =
        new(@"\b(?<code>IL\d{4}):\s+(?<aggregate>Assembly\s+)?'(?<origin>[^']+)'",
            System.Text.RegularExpressions.RegexOptions.Compiled);

    // ilc single-file analysis emits UNQUOTED member origins:
    // "IL3000: Ns.Type.Method(Args): message". Quoted forms contain a quote before
    // any parenthesis, so the negated class keeps the two patterns disjoint.
    // Generic type names carry backtick arity suffixes BEFORE the parameter list
    // ("AssetManagement.Core.JsonSaver`1.JsonSaver`1(): ..."), so the backtick is
    // allowed ahead of '(' (it stays excluded from the quoted-form pattern).
    private static readonly System.Text.RegularExpressions.Regex AotUnquotedMemberWarningRegex =
        new(@"\b(?<code>IL\d{4}):\s+(?<origin>[^\r\n'""]+?\([^)]*\)):\s+\S",
            System.Text.RegularExpressions.RegexOptions.Compiled);

    // Property/event ACCESSOR origins have NO parameter list:
    // "IL2090: Ns.Type`1.Member.get: message" (e.g. CachedReflectionInfo *.get,
    // ComponentTemplateBase`1.DefaultValueType.get). Origin stops at the first
    // ": "; quotes, parentheses and colons are excluded so this pattern cannot
    // match the quoted or parameterized forms above.
    private static readonly System.Text.RegularExpressions.Regex AotUnquotedAccessorWarningRegex =
        new(@"\b(?<code>IL\d{4}):\s+(?<origin>[^\r\n"":(]+):\s+\S",
            System.Text.RegularExpressions.RegexOptions.Compiled);

    // Shape-agnostic catch-all: every IL code appearing anywhere in the publish
    // log. Used only to detect codes the exact-tuple regexes above failed to
    // parse - a warning in an unrecognized textual shape must fail the gate
    // instead of bypassing both the 'unexplained' and 'stale' assertions.
    private static readonly System.Text.RegularExpressions.Regex AnyIlCodeRegex =
        new(@"\bIL\d{4}\b", System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <summary>
    /// Exact (code -> origin) warning MULTISET documented in
    /// docs/02_ADR/static-runtime-composition.md. Third-party payload origins only;
    /// a first-party entry here must carry a written justification in the ADR -
    /// silent whitelisting is not allowed. Both gates assert emitted == documented
    /// as COUNT-SENSITIVE multiset equality, so a package update that adds/removes
    /// origins - or adds a warning inside an already-documented origin - fails the
    /// gate and forces the inventory (and its justifications) to be revisited.
    /// The gated publishes run with -p:TrimmerSingleWarn=false so ILC/trimmer emit
    /// individual warnings instead of per-assembly aggregates; the entries below are
    /// the exact MEMBER-level form re-pinned 2026-08-25 from the first un-suppressed
    /// gated Server/Client publishes.
    /// </summary>
    // Inventory below = EXACTLY what the un-suppressed (-p:TrimmerSingleWarn=false)
    // Server AOT publish emits (verified 2026-08-25); the Client publish emits these
    // same 85 member-level tuples plus the client-only set below and nothing else
    // (verified same day). Every entry's justification lives in
    // docs/02_ADR/static-runtime-composition.md (Warning inventory section).
    // Warnings emitted identically by BOTH Server and Client publishes (the
    // same third-party payloads are rooted by the shared engine core). A
    // warning that only one side emits belongs in the side-specific set below,
    // never here - a shared entry silently tightens the other side's gate.
    private static List<AotWarning> SharedDocumentedAotWarnings =
    [
        // Third-party payload assemblies shipping without trim/AOT annotations;
        // members are their internal reflection/XML surfaces.
        new("IL2026", "member:Newtonsoft.Json.Linq.JContainer.System.ComponentModel.ITypedList.GetItemProperties(PropertyDescriptor[])"),
        new("IL2026", "member:Newtonsoft.Json.Linq.JObject.GetMetaObject(Expression)"),
        new("IL2026", "member:Newtonsoft.Json.Linq.JToken.GetMetaObject(Expression)"),
        new("IL2026", "member:Newtonsoft.Json.Linq.JValue.GetMetaObject(Expression)"),
        new("IL2026", "member:Newtonsoft.Json.Linq.JValue.GetMetaObject(Expression)"),
        new("IL2026", "member:Newtonsoft.Json.Linq.JValue.System.IConvertible.ToType(Type,IFormatProvider)"),
        new("IL2026", "member:Newtonsoft.Json.Schema.JsonSchema.ToString()"),
        new("IL3050", "member:Newtonsoft.Json.Linq.JObject.GetMetaObject(Expression)"),
        new("IL3050", "member:Newtonsoft.Json.Linq.JToken.GetMetaObject(Expression)"),
        new("IL3050", "member:Newtonsoft.Json.Linq.JValue.GetMetaObject(Expression)"),
        new("IL3050", "member:Newtonsoft.Json.Linq.JValue.GetMetaObject(Expression)"),
        new("IL3050", "member:Newtonsoft.Json.Linq.JValue.System.IConvertible.ToType(Type,IFormatProvider)"),
        new("IL3050", "member:Newtonsoft.Json.Schema.JsonSchema.ToString()"),
        new("IL2026", "member:nkast.Aether.Physics2D.Common.WorldXmlDeserializer.ReadSimpleType(XMLFragmentElement,Type,Boolean)"),
        new("IL2026", "member:nkast.Aether.Physics2D.Common.WorldXmlDeserializer.ReadSimpleType(XMLFragmentElement,Type,Boolean)"),
        new("IL2026", "member:nkast.Aether.Physics2D.Common.WorldXmlSerializer.WriteDynamicType(Type,Object)"),
        new("IL2026", "member:nkast.Aether.Physics2D.Common.WorldXmlSerializer.WriteDynamicType(Type,Object)"),
        new("IL2057", "member:nkast.Aether.Physics2D.Common.WorldXmlDeserializer.ReadSimpleType(XMLFragmentElement,Type,Boolean)"),
        new("IL3050", "member:nkast.Aether.Physics2D.Common.WorldXmlDeserializer.ReadSimpleType(XMLFragmentElement,Type,Boolean)"),
        new("IL3050", "member:nkast.Aether.Physics2D.Common.WorldXmlDeserializer.ReadSimpleType(XMLFragmentElement,Type,Boolean)"),
        new("IL3050", "member:nkast.Aether.Physics2D.Common.WorldXmlSerializer.WriteDynamicType(Type,Object)"),
        new("IL3050", "member:nkast.Aether.Physics2D.Common.WorldXmlSerializer.WriteDynamicType(Type,Object)"),
        new("IL2055", "member:MoonSharp.Interpreter.Interop.Converters.TableConversions.ConvertTableToDictionaryOfGenericType(Type,Type,Type,Table)"),
        new("IL2055", "member:MoonSharp.Interpreter.Interop.Converters.TableConversions.ConvertTableToListOfGenericType(Type,Type,Table)"),
        new("IL2060", "member:MoonSharp.Interpreter.Interop.UserDataRegistries.ExtensionMethodsRegistry.InstantiateMethodInfo(MethodInfo,Type,Type,Type)"),
        new("IL2067", "member:MoonSharp.Interpreter.Interop.Converters.TableConversions.ConvertTableToArrayOfGenericType(Type,Type,Table)"),
        new("IL2067", "member:MoonSharp.Interpreter.Interop.Converters.TableConversions.ConvertTableToDictionaryOfGenericType(Type,Type,Type,Table)"),
        new("IL2067", "member:MoonSharp.Interpreter.Interop.Converters.TableConversions.ConvertTableToListOfGenericType(Type,Type,Table)"),
        new("IL2072", "member:MoonSharp.Interpreter.Interop.ValueTypeDefaultCtorMemberDescriptor.Execute(Script,Object,ScriptExecutionContext,CallbackArguments)"),
        new("IL2072", "member:MoonSharp.Interpreter.Interop.ValueTypeDefaultCtorMemberDescriptor.GetValue(Script,Object)"),
        new("IL2075", "member:MoonSharp.Interpreter.Compatibility.Frameworks.FrameworkClrBase.GetConstructors(Type)"),
        new("IL2075", "member:MoonSharp.Interpreter.Compatibility.Frameworks.FrameworkClrBase.GetEvents(Type)"),
        new("IL2075", "member:MoonSharp.Interpreter.Compatibility.Frameworks.FrameworkClrBase.GetFields(Type)"),
        new("IL2075", "member:MoonSharp.Interpreter.Compatibility.Frameworks.FrameworkClrBase.GetInterfaces(Type)"),
        new("IL2075", "member:MoonSharp.Interpreter.Compatibility.Frameworks.FrameworkClrBase.GetMethod(Type,String)"),
        new("IL2075", "member:MoonSharp.Interpreter.Compatibility.Frameworks.FrameworkClrBase.GetMethod(Type,String,Type[])"),
        new("IL2075", "member:MoonSharp.Interpreter.Compatibility.Frameworks.FrameworkClrBase.GetMethods(Type)"),
        new("IL2075", "member:MoonSharp.Interpreter.Compatibility.Frameworks.FrameworkClrBase.GetNestedTypes(Type)"),
        new("IL2075", "member:MoonSharp.Interpreter.Compatibility.Frameworks.FrameworkClrBase.GetProperties(Type)"),
        new("IL2075", "member:MoonSharp.Interpreter.Compatibility.Frameworks.FrameworkClrBase.GetProperty(Type,String)"),
        new("IL3050", "member:MoonSharp.Interpreter.Interop.Converters.TableConversions.ConvertTableToDictionaryOfGenericType(Type,Type,Type,Table)"),
        new("IL3050", "member:MoonSharp.Interpreter.Interop.Converters.TableConversions.ConvertTableToListOfGenericType(Type,Type,Table)"),
        new("IL3050", "member:MoonSharp.Interpreter.Interop.StandardEnumUserDataDescriptor.StandardEnumUserDataDescriptor(Type,String,String[],Object[],Type)"),
        new("IL3050", "member:MoonSharp.Interpreter.Interop.UserDataRegistries.ExtensionMethodsRegistry.InstantiateMethodInfo(MethodInfo,Type,Type,Type)"),
        new("IL2055", "member:DCFApixels.DragonECS.TypeMeta.TypeMeta(Type)"),
        new("IL2070", "member:DCFApixels.DragonECS.EcsDebugUtility.AutoToString(Object,Type,Boolean)"),
        new("IL2075", "member:DCFApixels.DragonECS.Core.Internal.JsonDebugger.ToJsonLog_Internal(Int32&,Object,StringBuilder,Dictionary`2<Object,Int32>,Int32,Int32,Boolean)"),
        new("IL2075", "member:DCFApixels.DragonECS.Core.Internal.JsonDebugger.ToJsonLog_Internal(Int32&,Object,StringBuilder,Dictionary`2<Object,Int32>,Int32,Int32,Boolean)"),
        new("IL2077", "member:DCFApixels.DragonECS.TypeMeta.IsHasCustomMeta(Type)"),
        new("IL2077", "member:DCFApixels.DragonECS.TypeMeta.TypeMeta(Type)"),
        new("IL3050", "member:DCFApixels.DragonECS.TypeMeta.TypeMeta(Type)"),
        // Microsoft.CSharp dynamic-code surface pulled in via rooted third-party metadata;
        // not exercised by the engine's static path (runtime gate proves it).
        new("IL3050", "member:Microsoft.CSharp.RuntimeBinder.ComInterop.ComObject.RcwToComObject(Expression)"),
        // System.Linq.Expressions dynamic call-site cache accessors (BCL dynamic-code
        // surface reachable only through the rooted third-party metadata; not
        // exercised on the static path - runtime gate proves it).
        new("IL3050", "member:System.Linq.Expressions.CachedReflectionInfo.DynamicObject_TryBinaryOperation.get"),
        new("IL3050", "member:System.Linq.Expressions.CachedReflectionInfo.DynamicObject_TryConvert.get"),
        new("IL3050", "member:System.Linq.Expressions.CachedReflectionInfo.DynamicObject_TryCreateInstance.get"),
        new("IL3050", "member:System.Linq.Expressions.CachedReflectionInfo.DynamicObject_TryDeleteIndex.get"),
        new("IL3050", "member:System.Linq.Expressions.CachedReflectionInfo.DynamicObject_TryDeleteMember.get"),
        new("IL3050", "member:System.Linq.Expressions.CachedReflectionInfo.DynamicObject_TryGetIndex.get"),
        new("IL3050", "member:System.Linq.Expressions.CachedReflectionInfo.DynamicObject_TryGetMember.get"),
        new("IL3050", "member:System.Linq.Expressions.CachedReflectionInfo.DynamicObject_TryInvoke.get"),
        new("IL3050", "member:System.Linq.Expressions.CachedReflectionInfo.DynamicObject_TryInvokeMember.get"),
        new("IL3050", "member:System.Linq.Expressions.CachedReflectionInfo.DynamicObject_TrySetIndex.get"),
        new("IL3050", "member:System.Linq.Expressions.CachedReflectionInfo.DynamicObject_TrySetMember.get"),
        new("IL3050", "member:System.Linq.Expressions.CachedReflectionInfo.DynamicObject_TryUnaryOperation.get"),
        // First-party Dynamic-mode-only discovery paths (Assembly.GetTypes,
        // Activator.CreateInstance, open-generic Autofac registration), analyzed but
        // unreachable under Static composition; deleting them is a tracked follow-up.
        new("IL2026", "member:Karpik.Engine.Core.Runner.DynamicCompositionDiscovery.AddProviderDescriptors<TProvider>(Assembly,Func`2<TProvider,ReadOnlySpan`1<EcsUpdateSystemDescriptor>>,List`1<EcsUpdateSystemDescriptor>)"),
        new("IL2026", "member:Karpik.Engine.Core.Runner.Program.<>c.<LoadDynamicModules>b__1_0(Assembly)"),
        new("IL2026", "member:ModuleLoader.LoadPrimaryAssembly(String,String)"),
        new("IL2062", "member:Karpik.Engine.Core.Runner.DynamicCompositionDiscovery.AddProviderDescriptors<TProvider>(Assembly,Func`2<TProvider,ReadOnlySpan`1<EcsUpdateSystemDescriptor>>,List`1<EcsUpdateSystemDescriptor>)"),
        new("IL2067", "member:Karpik.Engine.Core.Runner.DynamicCompositionDiscovery.<>c.<ActivateModuleInstallers>b__0_3(Type)"),
        new("IL2072", "member:Karpik.Engine.Core.Bootstrap.RegisterTypes(Type[])"),
        new("IL2072", "member:Karpik.Engine.Core.Runner.AttributedServiceRegistrar.Register(ContainerBuilder,IEnumerable`1<Type>,ModuleScope)"),
        new("IL2072", "member:Karpik.Engine.Core.Runner.SystemRegistry.RegisterTypes(ContainerBuilder)"),
        new("IL2075", "member:Karpik.Engine.Core.EngineRunner.FormatComponent(Object)"),
        new("IL2075", "member:Karpik.Engine.Core.EngineRunner.FormatComponent(Object)"),
        // First-party ECS hot-reload state pipeline (Newtonsoft snapshots) +
        // ComponentTemplate MakeGenericType fallback + aspect-type reflection;
        // replacement tracked as the source-generated ECS state serialization follow-up.
        new("IL2026", "member:Karpik.Engine.Shared.ECS.ComponentArrayConverter.ReadJson(JsonReader,Type,IEcsComponentMember[],Boolean,JsonSerializer)"),
        new("IL2026", "member:Karpik.Engine.Shared.ECS.ComponentArrayConverter.WriteJson(JsonWriter,IEcsComponentMember[],JsonSerializer)"),
        new("IL2026", "member:Karpik.Engine.Shared.ECS.EcsWorldExtensions.<FromSnapshot>d__2.MoveNext()"),
        new("IL2026", "member:Karpik.Engine.Shared.ECS.EcsWorldExtensions.<FromSnapshot>d__2.MoveNext()"),
        new("IL2026", "member:Karpik.Engine.Shared.ECS.EcsWorldExtensions.ToSnapshot(EcsWorld,ComponentArrayConverter)"),
        new("IL2026", "member:Karpik.Engine.Shared.ECS.EcsWorldExtensions.ToSnapshot(EcsWorld,ComponentArrayConverter)"),
        new("IL3050", "member:Karpik.Engine.Shared.ECS.ComponentArrayConverter.ReadJson(JsonReader,Type,IEcsComponentMember[],Boolean,JsonSerializer)"),
        new("IL3050", "member:Karpik.Engine.Shared.ECS.ComponentArrayConverter.WriteJson(JsonWriter,IEcsComponentMember[],JsonSerializer)"),
        new("IL3050", "member:Karpik.Engine.Shared.ECS.EcsWorldExtensions.<FromSnapshot>d__2.MoveNext()"),
        new("IL3050", "member:Karpik.Engine.Shared.ECS.EcsWorldExtensions.<FromSnapshot>d__2.MoveNext()"),
        new("IL3050", "member:Karpik.Engine.Shared.ECS.EcsWorldExtensions.ToSnapshot(EcsWorld,ComponentArrayConverter)"),
        new("IL3050", "member:Karpik.Engine.Shared.ECS.EcsWorldExtensions.ToSnapshot(EcsWorld,ComponentArrayConverter)"),
        new("IL2070", "member:Karpik.Engine.Shared.ECS.ToTemplateExtensions.ToComponentTemplate(IEcsComponent)"),
        new("IL2070", "member:Karpik.Engine.Shared.ECS.ToTemplateExtensions2.ToComponentTemplate(IEcsTagComponent)"),
        new("IL2076", "member:Karpik.Engine.Shared.ECS.ToTemplateExtensions.ToComponentTemplate(IEcsComponent)"),
        new("IL2076", "member:Karpik.Engine.Shared.ECS.ToTemplateExtensions2.ToComponentTemplate(IEcsTagComponent)"),
        new("IL3050", "member:Karpik.Engine.Shared.ECS.ToTemplateExtensions.ToComponentTemplate(IEcsComponent)"),
        new("IL3050", "member:Karpik.Engine.Shared.ECS.ToTemplateExtensions2.ToComponentTemplate(IEcsTagComponent)"),
        new("IL2075", "member:Karpik.Engine.Shared.SystemExecutionNode.GetAspectTypes(IEcsRunParallel)"),
        new("IL2075", "member:Karpik.Engine.Shared.SystemExecutionNode.GetAspectTypes(IEcsRunParallel)"),
        // First-party asset pipeline: Newtonsoft JsonLoader/JsonSaver content
        // pipeline + loose assembly name binding.
        new("IL2026", "member:Karpik.Engine.Shared.AssetManagement.Core.JsonSaver`1.JsonSaver`1()"),
        new("IL2026", "member:Karpik.Engine.Shared.AssetManagement.Core.JsonSaver`1.JsonSaver`1()"),
        new("IL3050", "member:Karpik.Engine.Shared.AssetManagement.Core.JsonSaver`1.JsonSaver`1()"),
        new("IL3050", "member:Karpik.Engine.Shared.AssetManagement.Core.JsonSaver`1.JsonSaver`1()"),
        new("IL2026", "member:Karpik.Engine.Shared.AssetManagement.Core.JsonLoader`2.JsonLoader`2()"),
        new("IL3050", "member:Karpik.Engine.Shared.AssetManagement.Core.JsonLoader`2.JsonLoader`2()"),
        new("IL2026", "member:Karpik.Engine.Shared.AssetManagement.Core.LooseAssemblyNameBinder.BindToType(String,String)"),
        new("IL2057", "member:Karpik.Engine.Shared.AssetManagement.Core.LooseAssemblyNameBinder.BindToType(String,String)"),
        // ComponentTemplateBase.DefaultValueType resolves template fields via
        // Type.GetField on the generic parameter - part of the documented
        // component-template reflection fallback (see ECS.Core justification);
        // runtime behavior proven by the Server gate's reload cycles.
        new("IL2090", "member:Karpik.Engine.Shared.ECS.ComponentTemplateBase`1.DefaultValueType.get"),
        new("IL2090", "member:Karpik.Engine.Shared.ECS.ComponentTemplateBase`1.DefaultValueType.get"),
        // LoggerModule - open-generic Autofac logger registration (Dynamic-mode DI path).
        new("IL3050", "member:Karpik.Engine.Shared.Log.LoggerModuleInstaller.OnRegisterServices(ContainerBuilder)")
    ];

    /// <summary>Server-publish-only warnings; currently none.</summary>
    private static readonly List<AotWarning> ServerOnlyDocumentedAotWarnings = [];

    private static List<AotWarning> ServerDocumentedAotWarnings =>
        CreateSideInventory(SharedDocumentedAotWarnings, ServerOnlyDocumentedAotWarnings);

    // Client-publish-only warnings; each carries its own justification.
    private static readonly List<AotWarning> ClientOnlyDocumentedAotWarnings =
    [
        // Silk.NET probes native dependency paths via Assembly.Location/CodeBase and
        // DependencyContext, which are empty/unsupported under single-file AOT;
        // natives are staged next to the executable so resolution succeeds
        // regardless - justified empirically by this passing runtime gate.
        new("IL3000", "member:Silk.NET.Core.Loader.DefaultPathResolver.<>c.<.cctor>b__24_3(String)"),
        new("IL3002", "member:Silk.NET.Core.Loader.DefaultPathResolver.<>c.<.cctor>b__24_3(String)"),
        new("IL3000", "member:Silk.NET.Core.Loader.DefaultPathResolver.TryLocateNativeAssetFromDeps(String,String&,String&)"),
        new("IL3002", "member:Silk.NET.Core.Loader.DefaultPathResolver.TryLocateNativeAssetFromDeps(String,String&,String&)"),
        new("IL3002", "member:Silk.NET.Core.Loader.DefaultPathResolver.TryLocateNativeAssetInRuntimesFolder(String,String,String&)"),
        new("IL3002", "member:Microsoft.Extensions.DependencyModel.DependencyContext..cctor()")
    ];

    private static List<AotWarning> ClientDocumentedAotWarnings =>
        CreateSideInventory(SharedDocumentedAotWarnings, ClientOnlyDocumentedAotWarnings);

    private static List<AotWarning> CreateSideInventory(
        List<AotWarning> shared, List<AotWarning> sideOnly)
    {
        // MULTISET concat: duplicate entries are significant - the gate compares
        // emitted vs documented warning MULTISETS exactly (count-sensitive per
        // (code, origin)), so the inventory must preserve multiplicity too.
        List<AotWarning> inventory = [.. shared, .. sideOnly];
        return inventory;
    }

    // Internal so the same assembly can unit-test the gate against synthetic
    // publish logs without running a NativeAOT publish.
    internal static void AssertAotWarningsMatchDocumentedInventory(
        ProcessResult publish, IReadOnlyList<AotWarning> documented)
    {
        string publishLog = publish.StandardOutput + publish.StandardError;

        // MULTISET parse: every warning line contributes one tuple, duplicates
        // included. Aggregate lines (IL2104/IL3053) collapse a whole assembly
        // into one tuple, so only multiplicity can detect a NEW warning inside
        // an ALREADY-whitelisted assembly (the tuple itself stays identical).
        var emitted = new List<AotWarning>();
        foreach (System.Text.RegularExpressions.Match match in AotWarningRegex.Matches(publishLog))
        {
            string origin = match.Groups["origin"].Value;
            if (match.Groups["aggregate"].Success)
            {
                origin = "assembly:" + origin;
            }
            else
            {
                origin = origin.Contains('(') ? "member:" + origin : "assembly:" + origin;
            }

            emitted.Add(new AotWarning(match.Groups["code"].Value, origin));
        }

        foreach (System.Text.RegularExpressions.Match match in AotUnquotedMemberWarningRegex.Matches(publishLog))
        {
            emitted.Add(new AotWarning(match.Groups["code"].Value, "member:" + match.Groups["origin"].Value));
        }

        foreach (System.Text.RegularExpressions.Match match in AotUnquotedAccessorWarningRegex.Matches(publishLog))
        {
            emitted.Add(new AotWarning(match.Groups["code"].Value, "member:" + match.Groups["origin"].Value));
        }

        // Canonical order for comparison: raw log emission order is not stable
        // across ilc/ILLink versions, but the warning MULTISET is. Sorting both
        // sides canonically turns the assertion into exact count-sensitive
        // multiset equality: any new/removed/duplicated warning ANYWHERE -
        // same code, same assembly, aggregate form included - breaks equality.
        static List<AotWarning> Canonical(IEnumerable<AotWarning> warnings) =>
            [.. warnings.OrderBy(w => w.Code, StringComparer.Ordinal)
                .ThenBy(w => w.Origin, StringComparer.Ordinal)];
        List<AotWarning> emittedCanonical = Canonical(emitted);
        List<AotWarning> documentedCanonical = Canonical(documented);

        static string Format(IEnumerable<AotWarning> warnings) =>
            "{ " + string.Join("; ", warnings
                .Select(w => $"({w.Code} -> {w.Origin})")) + " }";

        static List<AotWarning> MultisetExcept(
            IReadOnlyList<AotWarning> source,
            IReadOnlyList<AotWarning> excluded)
        {
            var remaining = new Dictionary<AotWarning, int>();
            foreach (AotWarning warning in excluded)
            {
                remaining[warning] = remaining.TryGetValue(warning, out int count) ? count + 1 : 1;
            }

            var difference = new List<AotWarning>();
            foreach (AotWarning warning in source)
            {
                if (remaining.TryGetValue(warning, out int count) && count > 0)
                {
                    remaining[warning] = count - 1;
                }
                else
                {
                    difference.Add(warning);
                }
            }

            return difference;
        }

        Assert.True(
            emittedCanonical.SequenceEqual(documentedCanonical),
            "NativeAOT publish warning MULTISET differs from the documented inventory " +
            "(docs/02_ADR/static-runtime-composition.md). The comparison is count-sensitive " +
            "per (code, origin): a changed multiplicity means a warning appeared or vanished " +
            "inside an already-documented origin - re-justify the inventory." + Environment.NewLine +
            "unexplained surplus: " + Format(MultisetExcept(emittedCanonical, documentedCanonical)) + Environment.NewLine +
            "stale missing:   " + Format(MultisetExcept(documentedCanonical, emittedCanonical)) + Environment.NewLine +
            "emitted:    " + Format(emittedCanonical) + Environment.NewLine +
            "documented: " + Format(documentedCanonical) + Environment.NewLine + publishLog);

        // Coverage fallback comparing CODE COUNTS, not code sets: an unparseable
        // line of an already-parsed code must not hide behind correctly parsed
        // warnings of that same code. Every catch-all occurrence must be
        // accounted for by a parsed tuple of the same code.
        var catchAllCounts = new SortedDictionary<string, int>(StringComparer.Ordinal);
        foreach (System.Text.RegularExpressions.Match match in AnyIlCodeRegex.Matches(publishLog))
        {
            catchAllCounts[match.Value] = catchAllCounts.TryGetValue(match.Value, out int count) ? count + 1 : 1;
        }

        var parsedCounts = new SortedDictionary<string, int>(StringComparer.Ordinal);
        foreach (AotWarning warning in emitted)
        {
            parsedCounts[warning.Code] = parsedCounts.TryGetValue(warning.Code, out int count) ? count + 1 : 1;
        }

        List<string> unparseable = [.. catchAllCounts
            .Where(pair => !parsedCounts.TryGetValue(pair.Key, out int parsed) || parsed < pair.Value)
            .Select(pair => pair.Value > 1
                ? $"{pair.Key} x{pair.Value} (parsed: {(parsedCounts.TryGetValue(pair.Key, out int p) ? p : 0)})"
                : pair.Key)];
        Assert.True(unparseable.Count == 0,
            "NativeAOT publish log contains IL warning occurrences in a textual shape the " +
            "gate parsers do not recognize; extend AotWarningRegex / " +
            "AotUnquotedMemberWarningRegex (and re-justify the inventory in " +
            "docs/02_ADR/static-runtime-composition.md)." + Environment.NewLine +
            "unparseable codes: " + string.Join(", ", unparseable) + Environment.NewLine +
            publishLog);
    }

    // The gate parses warnings via exact-tuple regexes. A publish warning
    // emitted in an unrecognized textual shape must still fail the gate -
    // neither 'unexplained' nor 'stale' would catch it on their own.
    [Fact]
    public void Aot_gate_fails_when_publish_log_contains_unparseable_il_warning_code()
    {
        const string log = """
            IL2104: Assembly 'MoonSharp.Interpreter' produced 2 warnings.
            IL2153: Unrecognized textual warning shape without quotes or parentheses
            """;
        var documented = new List<AotWarning>
        {
            new("IL2104", "assembly:MoonSharp.Interpreter")
        };

        Xunit.Sdk.TrueException failure = Assert.Throws<Xunit.Sdk.TrueException>(() =>
            AssertAotWarningsMatchDocumentedInventory(
                new ProcessResult(0, log, string.Empty),
                documented));
        Assert.Contains("IL2153", failure.Message, StringComparison.Ordinal);
    }

    // Aggregate warnings (IL2104/IL3053) collapse every underlying warning of an
    // assembly into ONE tuple. A NEW warning inside an ALREADY-whitelisted
    // assembly keeps the (code, origin) tuple identical, so only the emitted
    // MULTISET (tuple multiplicity) can detect it - a set comparison passes
    // silently while the inventory no longer describes reality.
    [Fact]
    public void Aot_gate_fails_when_second_aggregate_warning_from_whitelisted_assembly_changes_multiplicity()
    {
        const string log = """
            IL2104: Assembly 'MoonSharp.Interpreter' produced 1 warnings.
            IL2104: Assembly 'MoonSharp.Interpreter' produced 1 warnings.
            """;
        var documented = new List<AotWarning>
        {
            new("IL2104", "assembly:MoonSharp.Interpreter")
        };

        Xunit.Sdk.TrueException failure = Assert.Throws<Xunit.Sdk.TrueException>(() =>
            AssertAotWarningsMatchDocumentedInventory(
                new ProcessResult(0, log, string.Empty),
                documented));
        Assert.Contains("multiplicity", failure.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "unexplained surplus: { (IL2104 -> assembly:MoonSharp.Interpreter) }",
            failure.Message,
            StringComparison.Ordinal);
    }

    // The coverage fallback must compare code COUNTS, not code sets: an
    // unparseable line of an ALREADY-parsed code hides behind any correctly
    // parsed warning of that same code under a set comparison.
    [Fact]
    public void Aot_gate_fails_when_unparseable_line_hides_behind_parsed_code_count()
    {
        const string log = """
            IL2104: Assembly 'MoonSharp.Interpreter' produced 1 warnings.
            IL2104 !! unrecognized textual shape !!
            """;
        var documented = new List<AotWarning>
        {
            new("IL2104", "assembly:MoonSharp.Interpreter")
        };

        Xunit.Sdk.TrueException failure = Assert.Throws<Xunit.Sdk.TrueException>(() =>
            AssertAotWarningsMatchDocumentedInventory(
                new ProcessResult(0, log, string.Empty),
                documented));
        Assert.Contains("IL2104", failure.Message, StringComparison.Ordinal);
    }

    // Count-sensitive exact equality: a documented multiset matching the emitted
    // multiset tuple-for-tuple (including duplicate entries) must pass.
    [Fact]
    public void Aot_gate_passes_when_emitted_multiset_equals_documented_multiset_including_duplicates()
    {
        const string log = """
            IL2104: Assembly 'MoonSharp.Interpreter' produced 1 warnings.
            IL2104: Assembly 'MoonSharp.Interpreter' produced 1 warnings.
            IL3053: Assembly 'DragonECS' produced 1 warnings.
            """;
        var documented = new List<AotWarning>
        {
            new("IL3053", "assembly:DragonECS"),
            new("IL2104", "assembly:MoonSharp.Interpreter"),
            new("IL2104", "assembly:MoonSharp.Interpreter")
        };

        AssertAotWarningsMatchDocumentedInventory(
            new ProcessResult(0, log, string.Empty),
            documented);
    }

    // Generic member names carry backtick arity suffixes before the parameter
    // list; the unquoted-member regex must accept them (regression: the origin
    // char class used to exclude the backtick, leaving such warnings unparsed
    // and only catchable by the coverage fallback).
    [Fact]
    public void Aot_gate_parses_unquoted_member_origin_with_generic_backtick_name()
    {
        const string log = """
            C:\src\JsonSaver.cs(10): Trim analysis warning IL2026: Karpik.Engine.Shared.AssetManagement.Core.JsonSaver`1.JsonSaver`1(): Using member 'Newtonsoft.Json.JsonSerializer.JsonSerializer()' which has 'RequiresUnreferencedCodeAttribute'.
            """;
        var documented = new List<AotWarning>
        {
            new("IL2026", "member:Karpik.Engine.Shared.AssetManagement.Core.JsonSaver`1.JsonSaver`1()")
        };

        AssertAotWarningsMatchDocumentedInventory(
            new ProcessResult(0, log, string.Empty),
            documented);
    }

    // Property ACCESSOR origins carry no parameter list; a dedicated regex must
    // parse them (regression: IL2090 '...DefaultValueType.get' and the
    // CachedReflectionInfo '*.get' surfaces were unparseable before).
    [Fact]
    public void Aot_gate_parses_unquoted_accessor_origin_without_parameter_list()
    {
        const string log = """
            C:\src\IComponentTemplate.cs(45): Trim analysis warning IL2090: Karpik.Engine.Shared.ECS.ComponentTemplateBase`1.DefaultValueType.get: 'this' argument does not satisfy 'DynamicallyAccessedMemberTypes.PublicFields'.
            """;
        var documented = new List<AotWarning>
        {
            new("IL2090", "member:Karpik.Engine.Shared.ECS.ComponentTemplateBase`1.DefaultValueType.get")
        };

        AssertAotWarningsMatchDocumentedInventory(
            new ProcessResult(0, log, string.Empty),
            documented);
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
                    line => line.Contains("Created entity", StringComparison.Ordinal));

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
                // The template init system is idempotent on a restored world, so a
                // reload must not add entities: the world stays byte-stable.
                Assert.Equal(rebuiltSnapshot.TotalEntityCount, snapshotAfterReload.TotalEntityCount);

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
            "logger.LogInformation(\"Created entity {entity} with GameComponent(42). Total entities: {count}\", entity, world.Count);";
        Assert.True(source.Contains(anchor, StringComparison.Ordinal),
            $"Mutation anchor was not found in the materialized game source: {serverSystemSourcePath}");
        string mutated = source.Replace(
            anchor,
            anchor + Environment.NewLine +
            $"        logger.LogInformation(\"{mutationMarker}: rebuilt static host\");",
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
        Assert.Contains(output, line => line.Contains("Worker starting", StringComparison.Ordinal));
    }

    private void AssertNoOrphanHostProcesses(string gameName)
    {
        string[] forbiddenNames =
        [
            gameName + ".Server.Launcher"
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
