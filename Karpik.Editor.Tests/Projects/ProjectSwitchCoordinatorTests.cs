using Karpik.Editor;
using Karpik.Engine.ProjectModel;
using Xunit;

namespace Karpik.Editor.Tests.Projects;

public sealed class ProjectSwitchCoordinatorTests
{
    [Fact]
    public async Task SwitchAsync_EvaluatesRuntimeByDefault()
    {
        var events = new List<string>();
        var candidate = CreateContext("New.slnx", new ProjectGeneration(1));
        var opener = new FakeProjectOpenService(events, ProjectOpenResult.Success(candidate));
        await using var coordinator = new ProjectSwitchCoordinator(opener);

        await coordinator.SwitchAsync(candidate.SolutionPath, TestContext.Current.CancellationToken);

        Assert.True(opener.EvaluateRuntime);
    }

    [Fact]
    public async Task SwitchAsync_TearsDownOldContextInExactOrderBeforeOpeningCandidate()
    {
        var events = new List<string>();
        var oldLifetime = new RecordingLifetime(events);
        var old = CreateContext("Old.slnx", new ProjectGeneration(1), oldLifetime);
        var candidate = CreateContext("New.slnx", new ProjectGeneration(2), new RecordingLifetime(events));
        var opener = new FakeProjectOpenService(events, ProjectOpenResult.Success(candidate));
        var publisher = new RecordingPublisher(events);
        await using var coordinator = new ProjectSwitchCoordinator(opener, publisher, old);

        ProjectOpenResult result = await coordinator.SwitchAsync(
            candidate.SolutionPath,
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Same(candidate, coordinator.ActiveProject);
        Assert.Equal(
            [
                "cancel-build",
                "stop-clients",
                "stop-server",
                "dispose-services",
                "save-workspace",
                "dispose-context",
                "open",
                "publish"
            ],
            events);
    }

    [Fact]
    public async Task SwitchAsync_TearsDownOldContextThenReturnsHandoffWithoutOpeningCandidate()
    {
        var events = new List<string>();
        var old = CreateContext("Old.slnx", new ProjectGeneration(1), new RecordingLifetime(events));
        var opener = new FakeProjectOpenService(
            events,
            ProjectOpenResult.Failure("must not open"));
        var handoff = new RecordingHandoffService(
            events,
            ProjectHandoffResult.Requested("Different SDK."));
        await using var coordinator = new ProjectSwitchCoordinator(
            opener,
            initialContext: old,
            handoffService: handoff);

        ProjectOpenResult result = await coordinator.SwitchAsync(
            Path.GetFullPath("New.slnx"),
            TestContext.Current.CancellationToken);

        Assert.True(result.RequiresEditorHandoff);
        Assert.False(result.IsSuccess);
        Assert.Equal(0, opener.OpenCount);
        Assert.Equal(
            [
                "handoff",
                "cancel-build",
                "stop-clients",
                "stop-server",
                "dispose-services",
                "save-workspace",
                "dispose-context"
            ],
            events);
    }

    [Fact]
    public async Task SwitchAsync_WhenHandoffPreparationFails_KeepsTheActiveProject()
    {
        var events = new List<string>();
        var old = CreateContext("Old.slnx", new ProjectGeneration(1), new RecordingLifetime(events));
        var opener = new FakeProjectOpenService(
            events,
            ProjectOpenResult.Failure("must not open"));
        var handoff = new RecordingHandoffService(
            events,
            ProjectHandoffResult.Failure("handoff unavailable"));
        await using var coordinator = new ProjectSwitchCoordinator(
            opener,
            initialContext: old,
            handoffService: handoff);

        ProjectOpenResult result = await coordinator.SwitchAsync(
            Path.GetFullPath("Broken.slnx"),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.False(result.RequiresEditorHandoff);
        Assert.Same(old, coordinator.ActiveProject);
        Assert.True(coordinator.CommandsEnabled);
        Assert.Equal(0, opener.OpenCount);
        Assert.Equal(["handoff"], events);
    }

    [Fact]
    public async Task SwitchAsync_WhenTeardownFails_DoesNotOpenCandidateAndLeavesNoActiveProject()
    {
        var events = new List<string>();
        var lifetime = new FailOnceLifetime(events, "stop-server");
        var opener = new FakeProjectOpenService(
            events,
            ProjectOpenResult.Success(CreateContext("New.slnx", new ProjectGeneration(2))));
        await using var coordinator = new ProjectSwitchCoordinator(
            opener,
            initialContext: CreateContext("Old.slnx", new ProjectGeneration(1), lifetime));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => coordinator.SwitchAsync("New.slnx", TestContext.Current.CancellationToken));

        Assert.Null(coordinator.ActiveProject);
        Assert.Equal(["cancel-build", "stop-clients", "stop-server"], events);
        Assert.Equal(0, opener.OpenCount);
    }

    [Fact]
    public async Task SwitchAsync_WhenCandidateFails_LeavesNoActiveProject()
    {
        var opener = new FakeProjectOpenService(
            [],
            ProjectOpenResult.Failure("candidate rejected"));
        await using var coordinator = new ProjectSwitchCoordinator(opener);

        ProjectOpenResult result = await coordinator.SwitchAsync(
            "Broken.slnx",
            TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Null(coordinator.ActiveProject);
        Assert.False(coordinator.CommandsEnabled);
    }

    [Fact]
    public async Task SwitchAsync_WhenPublicationFails_DisposesCandidate()
    {
        var events = new List<string>();
        var candidateLifetime = new RecordingLifetime(events);
        var candidate = CreateContext("New.slnx", new ProjectGeneration(1), candidateLifetime);
        var publisher = new RecordingPublisher(events)
        {
            Exception = new InvalidOperationException("publication failed")
        };
        await using var coordinator = new ProjectSwitchCoordinator(
            new FakeProjectOpenService(events, ProjectOpenResult.Success(candidate)),
            publisher);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => coordinator.SwitchAsync(candidate.SolutionPath, TestContext.Current.CancellationToken));

        Assert.Null(coordinator.ActiveProject);
        Assert.Contains("dispose-context", events);
    }

    [Fact]
    public async Task SwitchAsync_RejectsConcurrentSwitchAndNewCommands()
    {
        var openGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var openStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var candidate = CreateContext("New.slnx", new ProjectGeneration(1));
        var opener = new FakeProjectOpenService([], ProjectOpenResult.Success(candidate))
        {
            OpenGate = openGate,
            OpenStarted = openStarted
        };
        await using var coordinator = new ProjectSwitchCoordinator(opener);

        Task<ProjectOpenResult> first = coordinator.SwitchAsync(
            candidate.SolutionPath,
            TestContext.Current.CancellationToken);
        await openStarted.Task.WaitAsync(
            TimeSpan.FromSeconds(5),
            TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => coordinator.SwitchAsync("Other.slnx", TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => coordinator.ExecuteCommandAsync(
                new ProjectGeneration(1),
                static (_, _) => Task.CompletedTask,
                TestContext.Current.CancellationToken));

        openGate.SetResult();
        await first;
    }

    [Fact]
    public async Task SwitchAsync_WaitsForCommandAlreadyInProgressBeforeTeardown()
    {
        var commandGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var commandStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var events = new List<string>();
        var old = CreateContext("Old.slnx", new ProjectGeneration(1), new RecordingLifetime(events));
        var candidate = CreateContext("New.slnx", new ProjectGeneration(2));
        var opener = new FakeProjectOpenService(events, ProjectOpenResult.Success(candidate));
        await using var coordinator = new ProjectSwitchCoordinator(opener, initialContext: old);
        Task command = coordinator.ExecuteCommandAsync(
            old.Generation,
            async (_, token) =>
            {
                commandStarted.SetResult();
                await commandGate.Task.WaitAsync(token);
            },
            TestContext.Current.CancellationToken);
        await commandStarted.Task.WaitAsync(
            TimeSpan.FromSeconds(5),
            TestContext.Current.CancellationToken);

        Task<ProjectOpenResult> switching = coordinator.SwitchAsync(
            candidate.SolutionPath,
            TestContext.Current.CancellationToken);
        await Task.Delay(30, TestContext.Current.CancellationToken);

        Assert.Equal(["cancel-build"], events);
        commandGate.SetResult();
        await command;
        await switching;
        Assert.Equal("cancel-build", events[0]);
    }

    [Fact]
    public async Task SwitchAsync_CancelsActiveBuildBeforeWaitingForCommandGate()
    {
        var events = new List<string>();
        var lifetime = new CancelingBuildLifetime(events);
        var old = CreateContext("Old.slnx", new ProjectGeneration(1), lifetime);
        var candidate = CreateContext("New.slnx", new ProjectGeneration(2));
        var opener = new FakeProjectOpenService(events, ProjectOpenResult.Success(candidate));
        await using var coordinator = new ProjectSwitchCoordinator(opener, initialContext: old);
        Task command = coordinator.ExecuteCommandAsync(
            old.Generation,
            (_, token) =>
            {
                lifetime.BuildStarted.TrySetResult();
                return lifetime.BuildCancelled.Task.WaitAsync(token);
            },
            TestContext.Current.CancellationToken);
        await lifetime.BuildStarted.Task.WaitAsync(
            TimeSpan.FromSeconds(5),
            TestContext.Current.CancellationToken);

        Task<ProjectOpenResult> switching = coordinator.SwitchAsync(
            candidate.SolutionPath,
            TestContext.Current.CancellationToken);
        ProjectOpenResult result = await switching.WaitAsync(
            TimeSpan.FromSeconds(5),
            TestContext.Current.CancellationToken);
        await command;

        Assert.True(result.IsSuccess);
        Assert.Equal("cancel-build", events[0]);
        Assert.Contains("open", events);
    }

    [Fact]
    public async Task SwitchAsync_CancellationDuringOpenDisposesCandidateAndDoesNotPublish()
    {
        var events = new List<string>();
        var lifetime = new RecordingLifetime(events);
        var candidate = CreateContext("New.slnx", new ProjectGeneration(1), lifetime);
        var opener = new FakeProjectOpenService(events, ProjectOpenResult.Success(candidate))
        {
            CancelAfterCreatingCandidate = true
        };
        await using var coordinator = new ProjectSwitchCoordinator(opener);
        using var cancellation = new CancellationTokenSource();
        opener.Cancellation = cancellation;

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => coordinator.SwitchAsync(candidate.SolutionPath, cancellation.Token));

        Assert.Null(coordinator.ActiveProject);
        Assert.Contains("dispose-context", events);
    }

    [Fact]
    public async Task GenerationRejectsStaleCommandsAndOutputsAfterSwitch()
    {
        var first = CreateContext("First.slnx", new ProjectGeneration(1));
        var second = CreateContext("Second.slnx", new ProjectGeneration(2));
        var opener = new QueueProjectOpenService(
            ProjectOpenResult.Success(first),
            ProjectOpenResult.Success(second));
        await using var coordinator = new ProjectSwitchCoordinator(opener);

        await coordinator.SwitchAsync(first.SolutionPath, TestContext.Current.CancellationToken);
        ProjectGeneration stale = first.Generation;
        await coordinator.SwitchAsync(second.SolutionPath, TestContext.Current.CancellationToken);

        Assert.False(coordinator.IsCurrent(stale));
        Assert.False(coordinator.TryAcceptOutput(stale));
        Assert.True(coordinator.IsCurrent(second.Generation));
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => coordinator.ExecuteCommandAsync(
                stale,
                static (_, _) => Task.CompletedTask,
            TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ActiveContext_DisposeFailureCanBeRetried()
    {
        var lifetime = new RetryDisposeLifetime();
        ActiveProjectContext context = CreateContext(
            "Retry.slnx",
            new ProjectGeneration(1),
            lifetime);

        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await context.DisposeAsync());
        Assert.False(context.IsDisposed);

        await context.DisposeAsync();

        Assert.True(context.IsDisposed);
        Assert.Equal(2, lifetime.DisposeCount);
    }

    [Fact]
    public async Task DisposeAsync_UsesTheSameExactFullTeardownOrderAsSwitch()
    {
        var events = new List<string>();
        var context = CreateContext(
            "Shutdown.slnx",
            new ProjectGeneration(1),
            new RecordingLifetime(events));
        var coordinator = new ProjectSwitchCoordinator(
            new FakeProjectOpenService(events, ProjectOpenResult.Failure("unused")),
            initialContext: context);

        await coordinator.DisposeAsync();

        Assert.Equal(
            [
                "cancel-build",
                "stop-clients",
                "stop-server",
                "dispose-services",
                "save-workspace",
                "dispose-context"
            ],
            events);
        Assert.Null(coordinator.ActiveProject);
        Assert.False(coordinator.CommandsEnabled);
    }

    [Fact]
    public async Task DisposeAsync_WhenTeardownFails_RetainsOwnershipAndRetryCompletesCleanup()
    {
        var events = new List<string>();
        var lifetime = new FailOnceLifetime(events, "stop-server");
        var context = CreateContext("Shutdown.slnx", new ProjectGeneration(1), lifetime);
        var coordinator = new ProjectSwitchCoordinator(
            new FakeProjectOpenService(events, ProjectOpenResult.Failure("unused")),
            initialContext: context);

        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await coordinator.DisposeAsync());
        Assert.Null(coordinator.ActiveProject);
        Assert.False(coordinator.CommandsEnabled);

        await coordinator.DisposeAsync();

        Assert.Equal(
            [
                "cancel-build",
                "stop-clients",
                "stop-server",
                "cancel-build",
                "stop-clients",
                "stop-server",
                "dispose-services",
                "save-workspace",
                "dispose-context"
            ],
            events);
        Assert.Equal(1, lifetime.DisposeCount);
    }

    [Fact]
    public async Task DisposeAsync_ConcurrentCallsSerializeAndTeardownOnlyOnce()
    {
        var events = new List<string>();
        var lifetime = new GatedShutdownLifetime(events);
        var context = CreateContext("Shutdown.slnx", new ProjectGeneration(1), lifetime);
        var coordinator = new ProjectSwitchCoordinator(
            new FakeProjectOpenService(events, ProjectOpenResult.Failure("unused")),
            initialContext: context);

        Task first = coordinator.DisposeAsync().AsTask();
        await lifetime.CancelStarted.Task.WaitAsync(
            TimeSpan.FromSeconds(5),
            TestContext.Current.CancellationToken);
        Task second = coordinator.DisposeAsync().AsTask();
        await Task.Delay(30, TestContext.Current.CancellationToken);

        Assert.Equal(["cancel-build"], events);
        lifetime.CancelGate.SetResult();
        await Task.WhenAll(first, second);

        Assert.Equal(
            [
                "cancel-build",
                "stop-clients",
                "stop-server",
                "dispose-services",
                "save-workspace",
                "dispose-context"
            ],
            events);
    }

    private static ActiveProjectContext CreateContext(
        string name,
        ProjectGeneration generation,
        IActiveProjectLifetime? lifetime = null)
    {
        string solutionPath = Path.GetFullPath(Path.Combine(Path.GetTempPath(), name));
        var model = new KarpikSolutionModel(solutionPath, "0.6.0-test", []);
        string root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "engine"));
        var runtime = new ProjectRuntimeDescriptor(
            root,
            Path.Combine(root, "client-bundle"),
            Path.Combine(root, "server-bundle"),
            Path.Combine(root, "runners", "client", "runner.exe"),
            Path.Combine(root, "runners", "server", "runner.exe"));
        return new ActiveProjectContext(model, runtime, generation, lifetime ?? new RecordingLifetime([]));
    }

    private sealed class RecordingLifetime(List<string> events) : IActiveProjectLifetime
    {
        public string? FailureStep { get; init; }

        public Task CancelActiveBuildAsync(CancellationToken cancellationToken) => Step("cancel-build");
        public Task StopClientsAsync(CancellationToken cancellationToken) => Step("stop-clients");
        public Task StopServerAsync(CancellationToken cancellationToken) => Step("stop-server");
        public Task DisposeProjectServicesAsync(CancellationToken cancellationToken) => Step("dispose-services");
        public Task SaveWorkspaceAsync(string solutionPath, CancellationToken cancellationToken) => Step("save-workspace");

        public ValueTask DisposeAsync()
        {
            events.Add("dispose-context");
            return ValueTask.CompletedTask;
        }

        private Task Step(string name)
        {
            events.Add(name);
            return FailureStep == name
                ? Task.FromException(new InvalidOperationException(name))
                : Task.CompletedTask;
        }
    }

    private sealed class FakeProjectOpenService(
        List<string> events,
        ProjectOpenResult result) : IProjectOpenService
    {
        public int OpenCount { get; private set; }
        public bool EvaluateRuntime { get; private set; }
        public TaskCompletionSource? OpenGate { get; init; }
        public TaskCompletionSource? OpenStarted { get; init; }
        public bool CancelAfterCreatingCandidate { get; init; }
        public CancellationTokenSource? Cancellation { get; set; }

        public async Task<ProjectOpenResult> OpenAsync(
            string solutionPath,
            ProjectGeneration generation,
            CancellationToken cancellationToken) =>
            await OpenAsync(solutionPath, generation, cancellationToken, evaluateRuntime: true);

        public async Task<ProjectOpenResult> OpenAsync(
            string solutionPath,
            ProjectGeneration generation,
            CancellationToken cancellationToken,
            bool evaluateRuntime)
        {
            OpenCount++;
            EvaluateRuntime = evaluateRuntime;
            events.Add("open");
            OpenStarted?.TrySetResult();
            if (OpenGate is not null)
            {
                await OpenGate.Task.WaitAsync(cancellationToken);
            }
            if (CancelAfterCreatingCandidate)
            {
                Cancellation!.Cancel();
            }
            return result;
        }
    }

    private sealed class QueueProjectOpenService(params ProjectOpenResult[] results) : IProjectOpenService
    {
        private readonly Queue<ProjectOpenResult> _results = new(results);

        public Task<ProjectOpenResult> OpenAsync(
            string solutionPath,
            ProjectGeneration generation,
            CancellationToken cancellationToken) =>
            Task.FromResult(_results.Dequeue());
    }

    private sealed class RecordingPublisher(List<string> events) : IActiveProjectPublisher
    {
        public Exception? Exception { get; init; }

        public Task PublishAsync(ActiveProjectContext context, CancellationToken cancellationToken)
        {
            events.Add("publish");
            return Exception is null ? Task.CompletedTask : Task.FromException(Exception);
        }
    }

    private sealed class RecordingHandoffService(
        List<string> events,
        ProjectHandoffResult result) : IProjectHandoffService
    {
        public ProjectHandoffResult Prepare(string solutionPath)
        {
            events.Add("handoff");
            return result;
        }
    }

    private sealed class RetryDisposeLifetime : IActiveProjectLifetime
    {
        public int DisposeCount { get; private set; }
        public Task CancelActiveBuildAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task StopClientsAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task StopServerAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task DisposeProjectServicesAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task SaveWorkspaceAsync(string solutionPath, CancellationToken cancellationToken) => Task.CompletedTask;

        public ValueTask DisposeAsync()
        {
            DisposeCount++;
            return DisposeCount == 1
                ? ValueTask.FromException(new InvalidOperationException("first dispose failed"))
                : ValueTask.CompletedTask;
        }
    }

    private sealed class CancelingBuildLifetime(List<string> events) : IActiveProjectLifetime
    {
        public TaskCompletionSource BuildStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource BuildCancelled { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task CancelActiveBuildAsync(CancellationToken cancellationToken)
        {
            events.Add("cancel-build");
            BuildCancelled.TrySetResult();
            return Task.CompletedTask;
        }

        public Task StopClientsAsync(CancellationToken cancellationToken)
        {
            events.Add("stop-clients");
            return Task.CompletedTask;
        }

        public Task StopServerAsync(CancellationToken cancellationToken)
        {
            events.Add("stop-server");
            return Task.CompletedTask;
        }

        public Task DisposeProjectServicesAsync(CancellationToken cancellationToken)
        {
            events.Add("dispose-services");
            return Task.CompletedTask;
        }

        public Task SaveWorkspaceAsync(string solutionPath, CancellationToken cancellationToken)
        {
            events.Add("save-workspace");
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            events.Add("dispose-context");
            return ValueTask.CompletedTask;
        }
    }

    private sealed class FailOnceLifetime(List<string> events, string failureStep) : IActiveProjectLifetime
    {
        private int _failureRemaining = 1;
        public int DisposeCount { get; private set; }

        public Task CancelActiveBuildAsync(CancellationToken cancellationToken) => Step("cancel-build");
        public Task StopClientsAsync(CancellationToken cancellationToken) => Step("stop-clients");
        public Task StopServerAsync(CancellationToken cancellationToken) => Step("stop-server");
        public Task DisposeProjectServicesAsync(CancellationToken cancellationToken) => Step("dispose-services");
        public Task SaveWorkspaceAsync(string solutionPath, CancellationToken cancellationToken) => Step("save-workspace");

        public ValueTask DisposeAsync()
        {
            DisposeCount++;
            events.Add("dispose-context");
            return ValueTask.CompletedTask;
        }

        private Task Step(string name)
        {
            events.Add(name);
            if (name == failureStep && Interlocked.Exchange(ref _failureRemaining, 0) == 1)
            {
                return Task.FromException(new InvalidOperationException(name));
            }
            return Task.CompletedTask;
        }
    }

    private sealed class GatedShutdownLifetime(List<string> events) : IActiveProjectLifetime
    {
        public TaskCompletionSource CancelStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource CancelGate { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task CancelActiveBuildAsync(CancellationToken cancellationToken)
        {
            events.Add("cancel-build");
            CancelStarted.SetResult();
            await CancelGate.Task.WaitAsync(cancellationToken);
        }

        public Task StopClientsAsync(CancellationToken cancellationToken) => Step("stop-clients");
        public Task StopServerAsync(CancellationToken cancellationToken) => Step("stop-server");
        public Task DisposeProjectServicesAsync(CancellationToken cancellationToken) => Step("dispose-services");
        public Task SaveWorkspaceAsync(string solutionPath, CancellationToken cancellationToken) => Step("save-workspace");

        public ValueTask DisposeAsync()
        {
            events.Add("dispose-context");
            return ValueTask.CompletedTask;
        }

        private Task Step(string name)
        {
            events.Add(name);
            return Task.CompletedTask;
        }
    }
}
