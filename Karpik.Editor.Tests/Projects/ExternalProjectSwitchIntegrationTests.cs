using System.Diagnostics;
using System.Security;
using Karpik.Editor;
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
            EditorProjectLifetime firstLifetime = Assert.Single(created).Lifetime;
            int[] firstPids = firstLifetime.SessionManager.Sessions
                .Select(session => Assert.IsType<int>(session.ProcessId))
                .ToArray();
            Assert.Equal(2, firstPids.Length);

            ProjectOpenResult secondOpen = await viewModel.OpenProjectAsync(
                secondSolution,
                TestContext.Current.CancellationToken);
            Assert.True(secondOpen.IsSuccess, string.Join(Environment.NewLine, secondOpen.Diagnostics));
            await AssertProcessesExitedAsync(firstPids);
            Assert.Equal(Path.GetFullPath(secondSolution), viewModel.ProjectPath);
            Assert.Collection(
                created,
                first =>
                {
                    Assert.Equal(Path.GetFullPath(firstSolution), first.SolutionPath);
                    Assert.StartsWith(Path.GetFullPath(firstRoot), first.Runtime.ClientBundlePath);
                    Assert.StartsWith(Path.GetFullPath(firstRoot), first.Runtime.ServerBundlePath);
                },
                second =>
                {
                    Assert.Equal(Path.GetFullPath(secondSolution), second.SolutionPath);
                    Assert.StartsWith(Path.GetFullPath(secondRoot), second.Runtime.ClientBundlePath);
                    Assert.StartsWith(Path.GetFullPath(secondRoot), second.Runtime.ServerBundlePath);
                });

            await viewModel.StartServerCommand.Execute().FirstAsync().ToTask(TestContext.Current.CancellationToken);
            EditorProjectLifetime secondLifetime = created[1].Lifetime;
            int secondPid = Assert.IsType<int>(Assert.Single(secondLifetime.SessionManager.Sessions).ProcessId);
            await viewModel.ShutdownAsync();
            await AssertProcessesExitedAsync([secondPid]);
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
