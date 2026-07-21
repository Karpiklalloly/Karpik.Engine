using System.ComponentModel;
using System.Diagnostics;
using System.Security;
using Karpik.Editor;
using Karpik.Engine.Core;
using Karpik.Engine.ProjectModel;
using Karpik.Engine.Tooling;
using ReactiveUI.Builder;
using System.Reactive.Linq;
using System.Reactive.Threading.Tasks;
using Xunit;

namespace Karpik.Editor.Tests.Projects;

public sealed class ExternalProjectSwitchIntegrationTests
{
    private static readonly TimeSpan CommandTimeout = TimeSpan.FromMinutes(5);

    static ExternalProjectSwitchIntegrationTests()
    {
        RxAppBuilder.CreateReactiveUIBuilder()
            .WithCoreServices()
            .BuildApp();
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ExternalProjectSwitch_StopsOldWorkersAndUsesTheNewGameBundles()
    {
        Assert.SkipUnless(
            Environment.GetEnvironmentVariable("KARPIK_RUN_EDITOR_PROJECT_SWITCH_INTEGRATION") == "1",
            "Set KARPIK_RUN_EDITOR_PROJECT_SWITCH_INTEGRATION=1 to run the real external-project switch.");

        string repositoryRoot = GetRepositoryRoot();
        string engineRoot = ResolveEngineRoot(repositoryRoot);
        string packageFeed = Path.Combine(repositoryRoot, "artifacts", "nuget");
        Assert.True(
            File.Exists(Path.Combine(packageFeed, "Karpik.Engine.Sdk.0.6.0-local.nupkg")),
            "Pack Karpik.Engine.Sdk 0.6.0-local before running the editor switch integration.");
        string temporaryRoot = Path.Combine(
            Path.GetTempPath(),
            $"KarpikEditorSwitch-{Guid.NewGuid():N}");
        Directory.CreateDirectory(temporaryRoot);

        try
        {
            string firstRoot = MaterializeGame(repositoryRoot, temporaryRoot, "FirstGame");
            string secondRoot = MaterializeGame(repositoryRoot, temporaryRoot, "SecondGame");
            await BuildGameAsync(firstRoot, packageFeed, engineRoot);
            await BuildGameAsync(secondRoot, packageFeed, engineRoot);

            var created = new List<(string SolutionPath, ProjectRuntimeDescriptor Runtime, EditorProjectLifetime Lifetime)>();
            var contextFactory = new ActiveProjectContextFactory(
                (solution, runtime) =>
                {
                    var lifetime = new EditorProjectLifetime(
                        new WorkspaceStore(Path.Combine(temporaryRoot, "workspace.json")),
                        solution,
                        runtime);
                    created.Add((solution.SolutionPath, runtime, lifetime));
                    return lifetime;
                });
            var opener = new ProjectOpenService(
                installationProvider: new EngineInstallationProvider(explicitEngineRoot: engineRoot),
                contextFactory: contextFactory);
            using var viewModel = new EditorShellViewModel(
                new WorkspaceStore(Path.Combine(temporaryRoot, "workspace.json")),
                opener);
            string firstSolution = Path.Combine(firstRoot, "KarpikGame.slnx");
            string secondSolution = Path.Combine(secondRoot, "KarpikGame.slnx");

            ProjectOpenResult firstOpen = await viewModel.OpenProjectAsync(
                firstSolution,
                TestContext.Current.CancellationToken);
            Assert.True(firstOpen.IsSuccess, string.Join(Environment.NewLine, firstOpen.Diagnostics));
            await viewModel.StartServerCommand.Execute().FirstAsync().ToTask(TestContext.Current.CancellationToken);
            await viewModel.AddClientCommand.Execute().FirstAsync().ToTask(TestContext.Current.CancellationToken);
            await viewModel.AddClientCommand.Execute().FirstAsync().ToTask(TestContext.Current.CancellationToken);
            EditorProjectLifetime firstLifetime = Assert.Single(created).Lifetime;
            int[] firstPids = firstLifetime.SessionManager.Sessions
                .Select(session => Assert.IsType<int>(session.ProcessId))
                .ToArray();
            Assert.Equal(
                [Side.Server, Side.Client, Side.Client],
                firstLifetime.SessionManager.Sessions.Select(session => session.Side));
            Assert.Equal(3, firstPids.Length);
            Assert.Equal(3, firstPids.Distinct().Count());
            Assert.All(
                firstPids,
                pid => Assert.True(
                    IsProcessRunning(pid),
                    $"First-project worker PID {pid} exited before the switch."));
            ProjectRuntimeDescriptor firstRuntime = created[0].Runtime;
            AssertRuntimeOwnership(firstRuntime, firstRoot, engineRoot);

            bool secondActivationObserved = false;
            bool firstWorkersExitedBeforeSecondActivation = false;
            viewModel.PropertyChanged += ObserveSecondActivation;

            ProjectOpenResult secondOpen;
            try
            {
                secondOpen = await viewModel.OpenProjectAsync(
                    secondSolution,
                    TestContext.Current.CancellationToken);
            }
            finally
            {
                viewModel.PropertyChanged -= ObserveSecondActivation;
            }
            Assert.True(secondOpen.IsSuccess, string.Join(Environment.NewLine, secondOpen.Diagnostics));
            Assert.True(secondActivationObserved, "The second project was never published by the editor shell.");
            Assert.True(
                firstWorkersExitedBeforeSecondActivation,
                "The second project became active before every first-project worker exited.");
            await AssertProcessesExitedAsync(firstPids);
            AssertReloadTreesEmpty(firstRuntime);
            AssertRepresentativeBundleFilesAreExclusivelyOpenable(firstRuntime);
            Assert.Equal(Path.GetFullPath(secondSolution), viewModel.ProjectPath);
            Assert.Collection(
                created,
                first =>
                {
                    Assert.Equal(Path.GetFullPath(firstSolution), first.SolutionPath);
                    AssertRuntimeOwnership(first.Runtime, firstRoot, engineRoot);
                },
                second =>
                {
                    Assert.Equal(Path.GetFullPath(secondSolution), second.SolutionPath);
                    AssertRuntimeOwnership(second.Runtime, secondRoot, engineRoot);
                });

            await viewModel.StartServerCommand.Execute().FirstAsync().ToTask(TestContext.Current.CancellationToken);
            await viewModel.AddClientCommand.Execute().FirstAsync().ToTask(TestContext.Current.CancellationToken);
            await viewModel.AddClientCommand.Execute().FirstAsync().ToTask(TestContext.Current.CancellationToken);
            EditorProjectLifetime secondLifetime = created[1].Lifetime;
            int[] secondPids = secondLifetime.SessionManager.Sessions
                .Select(session => Assert.IsType<int>(session.ProcessId))
                .ToArray();
            Assert.Equal(
                [Side.Server, Side.Client, Side.Client],
                secondLifetime.SessionManager.Sessions.Select(session => session.Side));
            Assert.Equal(3, secondPids.Length);
            Assert.Equal(3, secondPids.Distinct().Count());
            Assert.All(
                secondPids,
                pid => Assert.True(
                    IsProcessRunning(pid),
                    $"Second-project worker PID {pid} exited before shutdown."));
            ProjectRuntimeDescriptor secondRuntime = created[1].Runtime;
            AssertRuntimeOwnership(secondRuntime, secondRoot, engineRoot);
            await viewModel.ShutdownAsync();
            await AssertProcessesExitedAsync(secondPids);
            AssertReloadTreesEmpty(secondRuntime);
            AssertRepresentativeBundleFilesAreExclusivelyOpenable(secondRuntime);

            void ObserveSecondActivation(object? sender, PropertyChangedEventArgs args)
            {
                if (args.PropertyName != nameof(EditorShellViewModel.ProjectPath)
                    || !PathEquals(viewModel.ProjectPath, secondSolution))
                {
                    return;
                }

                secondActivationObserved = true;
                firstWorkersExitedBeforeSecondActivation = firstPids.All(pid => !IsProcessRunning(pid));
            }
        }
        finally
        {
            Directory.Delete(temporaryRoot, recursive: true);
        }
    }

    private static string MaterializeGame(
        string repositoryRoot,
        string temporaryRoot,
        string name)
    {
        string source = Path.Combine(repositoryRoot, "templates", "Karpik.Game");
        string destination = Path.Combine(temporaryRoot, name);
        CopyDirectory(source, destination);
        File.WriteAllText(
            Path.Combine(destination, "KarpikGame.slnx"),
            """
            <Solution>
              <Folder Name="/Source/">
                <Project Path="Source/KarpikGame.Client/KarpikGame.Client.csproj" />
                <Project Path="Source/KarpikGame.Server/KarpikGame.Server.csproj" />
                <Project Path="Source/KarpikGame.Shared/KarpikGame.Shared.csproj" />
              </Folder>
            </Solution>
            """);
        return destination;
    }

    private static async Task BuildGameAsync(
        string gameRoot,
        string packageFeed,
        string engineRoot)
    {
        string configPath = Path.Combine(gameRoot, "NuGet.Config");
        File.WriteAllText(
            configPath,
            $"""
             <?xml version="1.0" encoding="utf-8"?>
             <configuration>
               <packageSources>
                 <clear />
                 <add key="karpik-local" value="{SecurityElement.Escape(packageFeed)}" />
               </packageSources>
               <config>
                 <add key="globalPackagesFolder" value="{SecurityElement.Escape(Path.Combine(gameRoot, ".packages"))}" />
               </config>
             </configuration>
             """);
        await RunDotNetAsync(
            gameRoot,
            engineRoot,
            ["restore", "KarpikGame.slnx", "-m:1", "-nr:false", "--configfile", configPath]);
        await RunDotNetAsync(
            gameRoot,
            engineRoot,
            ["build", "KarpikGame.slnx", "-m:1", "-nr:false", "--no-restore"]);
    }

    private static async Task RunDotNetAsync(
        string workingDirectory,
        string engineRoot,
        IReadOnlyList<string> arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }
        startInfo.Environment["KarpikEngineRoot"] = engineRoot;
        startInfo.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
        startInfo.Environment["DOTNET_CLI_USE_MSBUILD_SERVER"] = "0";
        startInfo.Environment["MSBUILDDISABLENODEREUSE"] = "1";
        startInfo.Environment["NuGetAudit"] = "false";

        using Process process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Unable to start dotnet.");
        Task<string> standardOutput = process.StandardOutput.ReadToEndAsync();
        Task<string> standardError = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(CommandTimeout);
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested)
        {
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync(CancellationToken.None);
            throw new TimeoutException($"dotnet {string.Join(' ', arguments)} timed out.");
        }
        string output = await standardOutput;
        string error = await standardError;
        Assert.True(
            process.ExitCode == 0,
            $"dotnet {string.Join(' ', arguments)} failed ({process.ExitCode}).{Environment.NewLine}{output}{Environment.NewLine}{error}");
    }

    private static async Task AssertProcessesExitedAsync(IEnumerable<int> processIds)
    {
        foreach (int processId in processIds)
        {
            DateTime deadline = DateTime.UtcNow + TimeSpan.FromSeconds(15);
            while (IsProcessRunning(processId) && DateTime.UtcNow < deadline)
            {
                await Task.Delay(50, TestContext.Current.CancellationToken);
            }
            Assert.False(IsProcessRunning(processId), $"Editor worker PID {processId} is still running.");
        }
    }

    private static void AssertRuntimeOwnership(
        ProjectRuntimeDescriptor runtime,
        string gameRoot,
        string engineRoot)
    {
        AssertPathEqual(
            Path.Combine(gameRoot, "Source", "KarpikGame.Client", "bin", "Debug", "net10.0", "karpik-bundle"),
            runtime.ClientBundlePath);
        AssertPathEqual(
            Path.Combine(gameRoot, "Source", "KarpikGame.Server", "bin", "Debug", "net10.0", "karpik-bundle"),
            runtime.ServerBundlePath);
        AssertPathEqual(
            Path.Combine(
                engineRoot,
                "runners",
                "client",
                OperatingSystem.IsWindows() ? "Karpik.Engine.Core.Runner.exe" : "Karpik.Engine.Core.Runner"),
            runtime.ClientRunnerPath);
        AssertPathEqual(
            Path.Combine(
                engineRoot,
                "runners",
                "server",
                OperatingSystem.IsWindows() ? "Karpik.Engine.Core.Runner.exe" : "Karpik.Engine.Core.Runner"),
            runtime.ServerRunnerPath);
    }

    private static void AssertReloadTreesEmpty(ProjectRuntimeDescriptor runtime)
    {
        AssertReloadTreeEmpty(runtime.ClientBundlePath, "state");
        AssertReloadTreeEmpty(runtime.ClientBundlePath, "shadow");
        AssertReloadTreeEmpty(runtime.ServerBundlePath, "state");
        AssertReloadTreeEmpty(runtime.ServerBundlePath, "shadow");
    }

    private static void AssertReloadTreeEmpty(string bundlePath, string treeName)
    {
        string path = Path.Combine(bundlePath, "reload", treeName);
        Assert.False(
            Directory.Exists(path)
            && Directory.EnumerateFileSystemEntries(path, "*", SearchOption.AllDirectories).Any(),
            $"Reload tree retained artifacts: {path}");
    }

    private static void AssertRepresentativeBundleFilesAreExclusivelyOpenable(
        ProjectRuntimeDescriptor runtime)
    {
        using FileStream client = OpenRepresentativeBundleFileExclusively(runtime.ClientBundlePath);
        using FileStream server = OpenRepresentativeBundleFileExclusively(runtime.ServerBundlePath);
    }

    private static FileStream OpenRepresentativeBundleFileExclusively(string bundlePath)
    {
        string modules = RuntimeBundleLayout.ResolveModuleDirectory(bundlePath);
        string representative = Path.Combine(
            modules,
            RuntimeBundleLayout.ReadCanonicalModuleManifest(modules)
                .First(name => name.StartsWith("KarpikGame.", StringComparison.Ordinal)));
        return File.Open(representative, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
    }

    private static void AssertPathEqual(string expected, string actual) =>
        Assert.True(
            PathEquals(expected, actual),
            $"Expected path '{Path.GetFullPath(expected)}', actual '{Path.GetFullPath(actual)}'.");

    private static bool PathEquals(string? first, string second) =>
        first is not null
        && string.Equals(Path.GetFullPath(first), Path.GetFullPath(second), PathComparison);

    private static bool IsProcessRunning(int processId)
    {
        try
        {
            using Process process = Process.GetProcessById(processId);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static string ResolveEngineRoot(string repositoryRoot)
    {
        string? explicitRoot = Environment.GetEnvironmentVariable("KarpikEngineRoot");
        if (!string.IsNullOrWhiteSpace(explicitRoot))
        {
            return Path.GetFullPath(explicitRoot);
        }
        string store = Path.Combine(repositoryRoot, "artifacts", "karpik-home", "Engines");
        var validator = new EngineInstallationValidator();
        string[] valid = Directory.EnumerateDirectories(store)
            .Where(path => validator.Validate(path, expectedSdkVersion: "0.6.0-local").IsValid)
            .ToArray();
        return Assert.Single(valid);
    }

    private static string GetRepositoryRoot() =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));

    private static StringComparison PathComparison => OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (string file in Directory.EnumerateFiles(source))
        {
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)));
        }
        foreach (string directory in Directory.EnumerateDirectories(source))
        {
            CopyDirectory(directory, Path.Combine(destination, Path.GetFileName(directory)));
        }
    }
}
