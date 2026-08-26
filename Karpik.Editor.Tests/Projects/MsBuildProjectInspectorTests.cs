using System.Diagnostics;
using Karpik.Editor;
using Karpik.Engine.ProjectModel;
using Xunit;

namespace Karpik.Editor.Tests.Projects;

public sealed class MsBuildProjectInspectorTests
{
    [Fact]
    public async Task InspectAsync_RealChildProcessEvaluatesSyntheticProject()
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            "KarpikEditorMsBuildInspectorTests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string engineRoot = Path.Combine(root, "engine");
            string sharedDirectory = Path.Combine(root, "Shared");
            string clientDirectory = Path.Combine(root, "Client");
            Directory.CreateDirectory(engineRoot);
            Directory.CreateDirectory(sharedDirectory);
            Directory.CreateDirectory(clientDirectory);
            string shared = Path.Combine(sharedDirectory, "Shared.csproj");
            string client = Path.Combine(clientDirectory, "Client.csproj");
            File.WriteAllText(shared, """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net10.0</TargetFramework>
                    <KarpikProjectKind>Runtime</KarpikProjectKind>
                    <KarpikSide>Shared</KarpikSide>
                  </PropertyGroup>
                </Project>
                """);
            File.WriteAllText(client, """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net10.0</TargetFramework>
                    <KarpikProjectKind>Runtime</KarpikProjectKind>
                    <KarpikSide>Client</KarpikSide>
                    <KarpikRuntimeBundlePath>$([System.IO.Path]::GetFullPath('$(MSBuildProjectDirectory)/bundle'))</KarpikRuntimeBundlePath>
                  </PropertyGroup>
                  <ItemGroup>
                    <ProjectReference Include="../Shared/Shared.csproj" />
                  </ItemGroup>
                </Project>
                """);
            string solutionPath = Path.Combine(root, "Game.slnx");
            File.WriteAllText(solutionPath, """
                <Solution>
                  <Project Path="Client/Client.csproj" />
                  <Project Path="Shared/Shared.csproj" />
                </Solution>
                """);
            File.WriteAllText(Path.Combine(root, "global.json"), "{}");
            var model = new KarpikSolutionModel(
                solutionPath,
                "test",
                [new KarpikProjectDescriptor(client, ["Karpik.Engine.Sdk"], KarpikProjectKind.Runtime,
                    KarpikProjectSide.Client, [shared], [])]);
            var inspector = new MsBuildProjectInspector(
                TimeSpan.FromSeconds(20),
                TimeSpan.FromSeconds(5));
            using ProjectInputLease lease = ProjectInputLease.Acquire(solutionPath);
            KarpikSolutionModel evaluation = lease.CreateEvaluationSolution(model);

            MsBuildProjectEvaluation result = Assert.Single(
                lease.RemapEvaluations(
                    await inspector.InspectAsync(
                        evaluation,
                        engineRoot,
                        TestContext.Current.CancellationToken)));

            Assert.Equal(Path.GetFullPath(client), result.ProjectPath);
            Assert.Equal(Path.GetFullPath(engineRoot), result.EngineRoot);
            Assert.Equal(Path.GetFullPath(Path.Combine(clientDirectory, "bundle")), result.RuntimeBundlePath);
            Assert.Equal([Path.GetFullPath(shared)], result.ProjectReferences);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task InspectAsync_ParsesExactEvaluatedPropertiesAndProjectReferenceItems()
    {
        string project = Path.GetFullPath("Client.csproj");
        string reference = Path.GetFullPath("Shared.csproj");
        var process = new FakeProcess
        {
            ResultJson = $$"""
                {
                  "Properties": {
                    "MSBuildProjectFullPath": "{{Escape(project)}}",
                    "KarpikProjectKind": "Runtime",
                    "KarpikSide": "Client",
                    "KarpikCompositionMode": "Static",
                    "KarpikRuntimeBundlePath": "{{Escape(Path.GetFullPath("bundle"))}}",
                    "KarpikEngineRoot": "{{Escape(Path.GetFullPath("engine"))}}",
                    "TargetPath": "{{Escape(Path.GetFullPath("Client.dll"))}}"
                  },
                  "Items": {
                    "ProjectReference": [
                      { "Identity": "Shared.csproj", "FullPath": "{{Escape(reference)}}" }
                    ]
                  }
                }
                """
        };
        var inspector = new MsBuildProjectInspector(
            new FakeProcessFactory(process),
            TimeSpan.FromSeconds(1),
            TimeSpan.FromSeconds(1));
        var solution = new KarpikSolutionModel(
            Path.GetFullPath("Game.slnx"),
            "0.6.0-test",
            [new KarpikProjectDescriptor(project, ["Karpik.Engine.Sdk"], KarpikProjectKind.Runtime,
                KarpikProjectSide.Client, [reference], [])]);

        IReadOnlyList<MsBuildProjectEvaluation> results = await inspector.InspectAsync(
            solution,
            Path.GetFullPath("engine"),
            TestContext.Current.CancellationToken);

        MsBuildProjectEvaluation result = Assert.Single(results);
        Assert.Equal(project, result.ProjectPath);
        Assert.Equal("Runtime", result.Kind);
        Assert.Equal("Client", result.Side);
        Assert.Equal("Static", result.CompositionMode);
        Assert.Equal([reference], result.ProjectReferences);
        Assert.Contains(
            process.StartInfo!.ArgumentList,
            argument => argument.StartsWith("-getProperty:", StringComparison.Ordinal));
        Assert.Contains("-getItem:ProjectReference", process.StartInfo.ArgumentList);
        Assert.Contains("-m:1", process.StartInfo.ArgumentList);
        Assert.Contains("-nr:false", process.StartInfo.ArgumentList);
    }

    [Fact]
    public async Task InspectAsync_WhenEvaluationTimesOut_KillsTreeAndConfirmsExit()
    {
        string project = Path.GetFullPath("Client.csproj");
        var process = new FakeProcess { WaitUntilKilled = true };
        var inspector = new MsBuildProjectInspector(
            new FakeProcessFactory(process),
            TimeSpan.FromMilliseconds(20),
            TimeSpan.FromSeconds(1));
        var solution = new KarpikSolutionModel(
            Path.GetFullPath("Game.slnx"),
            "0.6.0-test",
            [new KarpikProjectDescriptor(project, ["Karpik.Engine.Sdk"], KarpikProjectKind.Runtime,
                KarpikProjectSide.Client, [], [])]);

        await Assert.ThrowsAsync<TimeoutException>(
            () => inspector.InspectAsync(
                solution,
                Path.GetFullPath("engine"),
                TestContext.Current.CancellationToken));

        Assert.True(process.KillEntireTree);
        Assert.True(process.ExitConfirmedAfterKill);
    }

    [Fact]
    public async Task InspectAsync_WhenCallerCancels_KillsTreeAndPropagatesCancellation()
    {
        string project = Path.GetFullPath("Client.csproj");
        var process = new FakeProcess { WaitUntilKilled = true };
        var inspector = new MsBuildProjectInspector(
            new FakeProcessFactory(process),
            TimeSpan.FromSeconds(5),
            TimeSpan.FromSeconds(1));
        var solution = new KarpikSolutionModel(
            Path.GetFullPath("Game.slnx"),
            "0.6.0-test",
            [new KarpikProjectDescriptor(project, ["Karpik.Engine.Sdk"], KarpikProjectKind.Runtime,
                KarpikProjectSide.Client, [], [])]);
        using var cancellation = new CancellationTokenSource();
        cancellation.CancelAfter(TimeSpan.FromMilliseconds(20));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => inspector.InspectAsync(solution, Path.GetFullPath("engine"), cancellation.Token));

        Assert.True(process.KillEntireTree);
        Assert.True(process.ExitConfirmedAfterKill);
    }

    [Fact]
    public async Task InspectAsync_WhenTerminationIsUnconfirmed_RetainsProcessLeaseAndResultUntilReaperConfirmsExit()
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            "KarpikEditorMsBuildReaperTests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string? evaluationRoot = null;
        string? resultPath = null;
        try
        {
            string project = Path.Combine(root, "Client.csproj");
            string solutionPath = Path.Combine(root, "Game.slnx");
            File.WriteAllText(project, """<Project Sdk="Karpik.Engine.Sdk" />""");
            File.WriteAllText(solutionPath, """
                <Solution>
                  <Project Path="Client.csproj" />
                </Solution>
                """);
            File.WriteAllText(Path.Combine(root, "global.json"), """
                { "msbuild-sdks": { "Karpik.Engine.Sdk": "0.6.0-test" } }
                """);
            var process = new FakeProcess
            {
                WaitUntilKilled = true,
                BlockKillUntilExit = true,
                CreateResultImmediately = true
            };
            var inspector = new MsBuildProjectInspector(
                new FakeProcessFactory(process),
                TimeSpan.FromMilliseconds(20),
                TimeSpan.FromMilliseconds(20),
                maximumRetainedProcesses: 1);
            var model = new KarpikSolutionModel(
                solutionPath,
                "0.6.0-test",
                [new KarpikProjectDescriptor(
                    project,
                    ["Karpik.Engine.Sdk"],
                    KarpikProjectKind.Runtime,
                    KarpikProjectSide.Client,
                    [],
                    [])]);
            ProjectInputLease lease = ProjectInputLease.AcquirePortableMirror(solutionPath);
            evaluationRoot = Assert.IsType<string>(lease.EvaluationRoot);
            KarpikSolutionModel evaluation = lease.CreateEvaluationSolution(model);

            TimeoutException exception = await Assert.ThrowsAsync<TimeoutException>(
                () => inspector.InspectAsync(
                    evaluation,
                    root,
                    TestContext.Current.CancellationToken,
                    lease));
            resultPath = Assert.IsType<string>(process.ResultPath);
            lease.Dispose();

            Assert.Contains("background cleanup", exception.Message);
            Assert.Equal(1, inspector.RetainedProcessCount);
            Assert.False(process.Disposed);
            Assert.True(Directory.Exists(evaluationRoot));
            Assert.True(File.Exists(resultPath));

            process.ConfirmExit();
            using var drainTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            await inspector.DrainRetainedProcessesAsync(drainTimeout.Token);

            Assert.Equal(0, inspector.RetainedProcessCount);
            Assert.True(process.Disposed);
            Assert.False(Directory.Exists(evaluationRoot));
            Assert.False(File.Exists(resultPath));
        }
        finally
        {
            if (resultPath is not null && File.Exists(resultPath))
            {
                File.Delete(resultPath);
            }
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task InspectAsync_WhenRetainedProcessCapacityIsFull_RejectsBeforeStartingAnotherProcess()
    {
        string project = Path.GetFullPath("Client.csproj");
        var process = new FakeProcess
        {
            WaitUntilKilled = true,
            BlockKillUntilExit = true
        };
        var factory = new FakeProcessFactory(process);
        var inspector = new MsBuildProjectInspector(
            factory,
            TimeSpan.FromMilliseconds(20),
            TimeSpan.FromMilliseconds(20),
            maximumRetainedProcesses: 1);
        var solution = new KarpikSolutionModel(
            Path.GetFullPath("Game.slnx"),
            "0.6.0-test",
            [new KarpikProjectDescriptor(
                project,
                ["Karpik.Engine.Sdk"],
                KarpikProjectKind.Runtime,
                KarpikProjectSide.Client,
                [],
                [])]);

        await Assert.ThrowsAsync<TimeoutException>(
            () => inspector.InspectAsync(
                solution,
                Path.GetFullPath("engine"),
                TestContext.Current.CancellationToken));
        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => inspector.InspectAsync(
                solution,
                Path.GetFullPath("engine"),
                TestContext.Current.CancellationToken));

        Assert.Contains("capacity", exception.Message);
        Assert.Equal(1, factory.CallCount);
        Assert.Equal(1, inspector.RetainedProcessCount);

        process.ConfirmExit();
        using var drainTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        await inspector.DrainRetainedProcessesAsync(drainTimeout.Token);
    }

    [Fact]
    public async Task InspectAsync_RejectsOversizedResultWithoutUnboundedRead()
    {
        string project = Path.GetFullPath("Client.csproj");
        var process = new FakeProcess
        {
            ResultJson = new string('x', 1024 * 1024 + 1)
        };
        var inspector = new MsBuildProjectInspector(
            new FakeProcessFactory(process),
            TimeSpan.FromSeconds(1),
            TimeSpan.FromSeconds(1));
        var solution = new KarpikSolutionModel(
            Path.GetFullPath("Game.slnx"),
            "0.6.0-test",
            [new KarpikProjectDescriptor(project, ["Karpik.Engine.Sdk"], KarpikProjectKind.Runtime,
                KarpikProjectSide.Client, [], [])]);

        InvalidDataException exception = await Assert.ThrowsAsync<InvalidDataException>(
            () => inspector.InspectAsync(
                solution,
                Path.GetFullPath("engine"),
                TestContext.Current.CancellationToken));

        Assert.Contains("exceeds", exception.Message);
    }

    [Fact]
    public async Task InspectAsync_RejectsResultFileReplacedBySymbolicLink()
    {
        string project = Path.GetFullPath("Client.csproj");
        string target = Path.Combine(
            Path.GetTempPath(),
            $"karpik-msbuild-result-target-{Guid.NewGuid():N}.json");
        string probe = Path.Combine(
            Path.GetTempPath(),
            $"karpik-msbuild-result-probe-{Guid.NewGuid():N}.json");
        File.WriteAllText(target, "{}");
        try
        {
            try
            {
                File.CreateSymbolicLink(probe, target);
                File.Delete(probe);
            }
            catch (Exception exception) when (exception is UnauthorizedAccessException
                                             or IOException
                                             or PlatformNotSupportedException)
            {
                return;
            }

            var process = new FakeProcess { ResultLinkTarget = target };
            var inspector = new MsBuildProjectInspector(
                new FakeProcessFactory(process),
                TimeSpan.FromSeconds(1),
                TimeSpan.FromSeconds(1));
            var solution = new KarpikSolutionModel(
                Path.GetFullPath("Game.slnx"),
                "0.6.0-test",
                [new KarpikProjectDescriptor(project, ["Karpik.Engine.Sdk"], KarpikProjectKind.Runtime,
                    KarpikProjectSide.Client, [], [])]);

            await Assert.ThrowsAsync<InvalidDataException>(
                () => inspector.InspectAsync(
                    solution,
                    Path.GetFullPath("engine"),
                    TestContext.Current.CancellationToken));
        }
        finally
        {
            if (File.Exists(probe))
            {
                File.Delete(probe);
            }
            if (File.Exists(target))
            {
                File.Delete(target);
            }
        }
    }

    [Fact]
    public async Task InspectAsync_RemovesBrokenResultSymlinkWithoutFollowingIt()
    {
        string project = Path.GetFullPath("Client.csproj");
        string missingTarget = Path.Combine(
            Path.GetTempPath(),
            $"karpik-missing-result-{Guid.NewGuid():N}.json");
        string probe = Path.Combine(
            Path.GetTempPath(),
            $"karpik-broken-result-probe-{Guid.NewGuid():N}.json");
        try
        {
            try
            {
                File.CreateSymbolicLink(probe, missingTarget);
                File.Delete(probe);
            }
            catch (Exception exception) when (exception is UnauthorizedAccessException
                                             or IOException
                                             or PlatformNotSupportedException)
            {
                return;
            }

            var process = new FakeProcess { ResultLinkTarget = missingTarget };
            var inspector = new MsBuildProjectInspector(
                new FakeProcessFactory(process),
                TimeSpan.FromSeconds(1),
                TimeSpan.FromSeconds(1));
            var solution = new KarpikSolutionModel(
                Path.GetFullPath("Game.slnx"),
                "0.6.0-test",
                [new KarpikProjectDescriptor(project, ["Karpik.Engine.Sdk"], KarpikProjectKind.Runtime,
                    KarpikProjectSide.Client, [], [])]);

            await Assert.ThrowsAnyAsync<Exception>(
                () => inspector.InspectAsync(
                    solution,
                    Path.GetFullPath("engine"),
                    TestContext.Current.CancellationToken));

            Assert.Null(new FileInfo(process.ResultPath!).LinkTarget);
        }
        finally
        {
            if (File.Exists(probe) || new FileInfo(probe).LinkTarget is not null)
            {
                File.Delete(probe);
            }
        }
    }

    [Fact]
    public async Task InspectAsync_CleanupFailureDoesNotMaskPrimaryProcessDiagnostic()
    {
        string project = Path.GetFullPath("Client.csproj");
        var process = new FakeProcess
        {
            ExitCodeValue = 7,
            CreateResultDirectory = true
        };
        var inspector = new MsBuildProjectInspector(
            new FakeProcessFactory(process),
            TimeSpan.FromSeconds(1),
            TimeSpan.FromSeconds(1));
        var solution = new KarpikSolutionModel(
            Path.GetFullPath("Game.slnx"),
            "0.6.0-test",
            [new KarpikProjectDescriptor(project, ["Karpik.Engine.Sdk"], KarpikProjectKind.Runtime,
                KarpikProjectSide.Client, [], [])]);

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => inspector.InspectAsync(
                solution,
                Path.GetFullPath("engine"),
                TestContext.Current.CancellationToken));

        Assert.Contains("exit code 7", exception.Message);
        if (Directory.Exists(process.ResultPath))
        {
            Directory.Delete(process.ResultPath);
        }
    }

    private static string Escape(string value) => value.Replace("\\", "\\\\");

    private sealed class FakeProcessFactory(FakeProcess process) : IMsBuildProcessFactory
    {
        public int CallCount { get; private set; }

        public IMsBuildProcess Start(ProcessStartInfo startInfo)
        {
            CallCount++;
            process.StartInfo = startInfo;
            process.ResultPath = startInfo.ArgumentList
                .Single(argument => argument.StartsWith("-getResultOutputFile:", StringComparison.Ordinal))
                ["-getResultOutputFile:".Length..];
            if (process.CreateResultImmediately)
            {
                File.WriteAllText(process.ResultPath, process.ResultJson);
            }
            return process;
        }
    }

    private sealed class FakeProcess : IMsBuildProcess
    {
        private readonly TaskCompletionSource _killed =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public ProcessStartInfo? StartInfo { get; set; }
        public string? ResultPath { get; set; }
        public string ResultJson { get; init; } = "{}";
        public string? ResultLinkTarget { get; init; }
        public bool WaitUntilKilled { get; init; }
        public bool BlockKillUntilExit { get; init; }
        public bool CreateResultImmediately { get; init; }
        public bool KillEntireTree { get; private set; }
        public bool ExitConfirmedAfterKill { get; private set; }
        public bool Disposed { get; private set; }
        public int ExitCode => ExitCodeValue;
        public int ExitCodeValue { get; init; }
        public bool CreateResultDirectory { get; init; }

        public Task<string> ReadStandardOutputAsync(int maximumCharacters, CancellationToken cancellationToken) =>
            Task.FromResult("");

        public Task<string> ReadStandardErrorAsync(int maximumCharacters, CancellationToken cancellationToken) =>
            Task.FromResult("");

        public async Task WaitForExitAsync(CancellationToken cancellationToken)
        {
            if (WaitUntilKilled)
            {
                await _killed.Task.WaitAsync(cancellationToken);
                ExitConfirmedAfterKill = true;
                return;
            }
            if (CreateResultDirectory)
            {
                Directory.CreateDirectory(ResultPath!);
            }
            else if (ResultLinkTarget is not null)
            {
                File.CreateSymbolicLink(ResultPath!, ResultLinkTarget);
            }
            else
            {
                File.WriteAllText(ResultPath!, ResultJson);
            }
        }

        public void Kill(bool entireProcessTree)
        {
            KillEntireTree = entireProcessTree;
            if (BlockKillUntilExit)
            {
                _killed.Task.GetAwaiter().GetResult();
                return;
            }
            _killed.TrySetResult();
        }

        public void ConfirmExit() => _killed.TrySetResult();

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }
}
