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
                    "-p:PublishAot=true", "-p:InvariantGlobalization=true", "-m:1", "-nr:false"
                ],
                game.Environment,
                TimeSpan.FromMinutes(30));
            AssertSuccess(publish, "publish the Static Server host under NativeAOT");

            List<string> aotWarnings = AotWarningCodes(publish.StandardOutput + publish.StandardError);
            // Documented inventory: aggregate IL2104/IL3053 from unannotated engine
            // payload assemblies (see the launcher csproj NoWarn rationale and the ADR).
            string[] documentedCodes = ["IL2104", "IL3053"];
            List<string> unexplained = aotWarnings.Where(code => !documentedCodes.Contains(code)).ToList();
            Assert.True(unexplained.Count == 0,
                "NativeAOT publish emitted warnings outside the documented inventory " +
                "(IL2104/IL3053): " + string.Join(", ", unexplained) + Environment.NewLine + publish.StandardOutput);

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
                    "-p:PublishAot=true", "-p:InvariantGlobalization=true", "-m:1", "-nr:false"
                ],
                game.Environment,
                TimeSpan.FromMinutes(30));
            AssertSuccess(publish, "publish the Static Client host under NativeAOT");

            List<string> aotWarnings = AotWarningCodes(publish.StandardOutput + publish.StandardError);
            // Same documented inventory as the Server gate (launcher csproj NoWarn rationale).
            string[] clientDocumentedCodes = ["IL2104", "IL3053", "IL3000", "IL3002"];
            List<string> clientUnexplained = aotWarnings.Where(code => !clientDocumentedCodes.Contains(code)).ToList();
            Assert.True(clientUnexplained.Count == 0,
                "NativeAOT publish emitted warnings outside the documented inventory " +
                "(IL2104/IL3053): " + string.Join(", ", clientUnexplained) + Environment.NewLine + publish.StandardOutput);

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

    private static List<string> AotWarningCodes(string publishLog)
    {
        var codes = new SortedSet<string>(StringComparer.Ordinal);
        foreach (System.Text.RegularExpressions.Match match in
                 System.Text.RegularExpressions.Regex.Matches(publishLog, @"\bIL\d{4}\b"))
        {
            codes.Add(match.Value);
        }

        return [.. codes];
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
