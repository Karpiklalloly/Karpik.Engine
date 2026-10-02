using Karpik.Engine.Tooling;
using Karpik.Launcher.Services;
using Xunit;

namespace Karpik.Launcher.Tests;

public sealed class EditorProcessHostTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("\0")]
    public async Task RunAsyncReturnsResolutionFailureForAnInvalidInitialPath(string? invalidPath)
    {
        using var workspace = new TestWorkspace();
        var runner = new RecordingProcessRunner((_, _) => 0);
        EditorProcessHost host = CreateHost(
            Path.Combine(workspace.RootPath, "local"),
            runner,
            maximumHandoffs: 1);

        EditorHostResult result = await host.RunAsync(
            invalidPath!,
            TestContext.Current.CancellationToken);

        Assert.Equal(EditorHostCode.ResolutionFailure, result.Code);
        Assert.Empty(runner.Requests);
    }

    [Fact]
    public async Task RunAsyncFollowsAValidatedCrossVersionHandoffThenStopsOnNormalExit()
    {
        using var workspace = new TestWorkspace();
        string localRoot = Path.Combine(workspace.RootPath, "local");
        string firstRoot = workspace.CreateInstallation(localRoot, "engine-a", "1.0.0", "sdk-a");
        string secondRoot = workspace.CreateInstallation(localRoot, "engine-b", "2.0.0", "sdk-b");
        string first = workspace.CreateGame("First", "sdk-a");
        string second = workspace.CreateGame("Second", "sdk-b");
        var runner = new RecordingProcessRunner((request, invocation) =>
        {
            if (invocation == 1)
            {
                new EditorHandoffRequest(second).Write(GetArgumentValue(request.Arguments, "--handoff"));
                return EditorExitCodes.HandoffRequested;
            }
            return 0;
        });
        var host = CreateHost(localRoot, runner, maximumHandoffs: 3);

        EditorHostResult result = await host.RunAsync(first, TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess, result.Message);
        Assert.Equal(2, runner.Requests.Count);
        Assert.Equal(firstRoot, runner.Requests[0].Environment["KarpikEngineRoot"]);
        Assert.Equal(secondRoot, runner.Requests[1].Environment["KarpikEngineRoot"]);
        Assert.Equal(Path.GetFullPath(second), result.SolutionPath);
    }

    [Fact]
    public async Task RunAsyncRejectsMalformedHandoffAndDoesNotRetryOtherExitCodes()
    {
        using var workspace = new TestWorkspace();
        string localRoot = Path.Combine(workspace.RootPath, "local");
        workspace.CreateInstallation(localRoot, "engine-a", "1.0.0", "sdk-a");
        string solution = workspace.CreateGame("Game", "sdk-a");
        var malformed = new RecordingProcessRunner((request, _) =>
        {
            File.WriteAllText(GetArgumentValue(request.Arguments, "--handoff"), "{}");
            return EditorExitCodes.HandoffRequested;
        });

        EditorHostResult malformedResult = await CreateHost(localRoot, malformed, 3)
            .RunAsync(solution, TestContext.Current.CancellationToken);
        Assert.Equal(EditorHostCode.InvalidHandoff, malformedResult.Code);
        Assert.Single(malformed.Requests);

        var failed = new RecordingProcessRunner((_, _) => 7);
        EditorHostResult failedResult = await CreateHost(localRoot, failed, 3)
            .RunAsync(solution, TestContext.Current.CancellationToken);
        Assert.Equal(EditorHostCode.EditorFailed, failedResult.Code);
        Assert.Single(failed.Requests);
    }

    [Fact]
    public async Task RunAsyncBoundsHandoffRestarts()
    {
        using var workspace = new TestWorkspace();
        string localRoot = Path.Combine(workspace.RootPath, "local");
        workspace.CreateInstallation(localRoot, "engine-a", "1.0.0", "sdk-a");
        string solution = workspace.CreateGame("Game", "sdk-a");
        var runner = new RecordingProcessRunner((request, _) =>
        {
            new EditorHandoffRequest(solution).Write(GetArgumentValue(request.Arguments, "--handoff"));
            return EditorExitCodes.HandoffRequested;
        });

        EditorHostResult result = await CreateHost(localRoot, runner, maximumHandoffs: 2)
            .RunAsync(solution, TestContext.Current.CancellationToken);

        Assert.Equal(EditorHostCode.HandoffLimitExceeded, result.Code);
        Assert.Equal(3, runner.Requests.Count);
    }

    [Fact]
    public async Task RunAsyncUsesTheResolvedEditorWorkingDirectory()
    {
        using var workspace = new TestWorkspace();
        string localRoot = Path.Combine(workspace.RootPath, "local");
        workspace.CreateInstallation(localRoot, "engine", "1.0.0", "sdk");
        string solution = workspace.CreateGame("Game", "sdk");
        string sourceEditor = Path.Combine(workspace.RootPath, "source-editor");
        Directory.CreateDirectory(sourceEditor);
        File.WriteAllText(Path.Combine(sourceEditor, "Karpik.Editor.dll"), "editor");
        var runner = new RecordingProcessRunner((_, _) => 0);
        var resolver = new EditorResolver(
            new EngineInstallationResolver(localApplicationDataRoot: localRoot),
            debugEditorDirectory: sourceEditor);
        var host = new EditorProcessHost(resolver, runner, localRoot);

        EditorHostResult result = await host.RunAsync(solution, TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess, result.Message);
        Assert.Equal(sourceEditor, Assert.Single(runner.Requests).WorkingDirectory);
    }

    private static EditorProcessHost CreateHost(
        string localRoot,
        IEditorProcessRunner runner,
        int maximumHandoffs) =>
        new(
            new EditorResolver(new EngineInstallationResolver(localApplicationDataRoot: localRoot)),
            runner,
            localRoot,
            maximumHandoffs);

    private static string GetArgumentValue(IReadOnlyList<string> arguments, string name)
    {
        int index = arguments.IndexOf(name);
        Assert.True(index >= 0 && index + 1 < arguments.Count);
        return arguments[index + 1];
    }

    private sealed class RecordingProcessRunner(
        Func<EditorProcessStartRequest, int, int> run) : IEditorProcessRunner
    {
        public List<EditorProcessStartRequest> Requests { get; } = [];

        public Task<int> RunAsync(EditorProcessStartRequest request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(run(request, Requests.Count));
        }
    }
}

internal static class ReadOnlyListExtensions
{
    public static int IndexOf(this IReadOnlyList<string> values, string value)
    {
        for (int index = 0; index < values.Count; index++)
        {
            if (values[index] == value) return index;
        }
        return -1;
    }
}
