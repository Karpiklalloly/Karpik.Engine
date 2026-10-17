using Karpik.Engine.Core;
using Xunit;

[Collection(nameof(WorkingDirectoryCollection))]
public sealed class BootstrapBoundaryTests
{
    [Fact]
    public async Task Bootstrap_AwaitsRunnerStartupAndShutdown()
    {
        var runner = new AsyncRunnerStub();
        var bootstrap = new Bootstrap(Side.Server, runner);
        var isRunning = new Ref<bool>(true);

        MainThreadScheduler scheduler = bootstrap.Initialize(
            Environment.CurrentManagedThreadId,
            isRunning);
        scheduler.Execute();

        Assert.False(bootstrap.Startup.IsCompleted);
        runner.SetupGate.SetResult();
        await bootstrap.Startup;

        bootstrap.Loop(Application.TICK_DT);

        Task shutdown = bootstrap.ShutdownAsync();
        Assert.False(shutdown.IsCompleted);
        runner.DestroyGate.SetResult();
        await shutdown;
    }

    [Fact]
    public void ExplicitRunner_DoesNotResolveRunnerAssemblyFromWorkingDirectory()
    {
        string previousDirectory = Directory.GetCurrentDirectory();
        string isolatedDirectory = Path.Combine(Path.GetTempPath(), $"karpik-bootstrap-{Guid.NewGuid():N}");
        Directory.CreateDirectory(isolatedDirectory);

        try
        {
            Directory.SetCurrentDirectory(isolatedDirectory);

            _ = new Bootstrap(Side.Server, new EngineRunner());
        }
        finally
        {
            Directory.SetCurrentDirectory(previousDirectory);
            Directory.Delete(isolatedDirectory, recursive: true);
        }
    }
}

internal sealed class AsyncRunnerStub : IEngineRunner
{
    public readonly TaskCompletionSource SetupGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public readonly TaskCompletionSource DestroyGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool _started;

    public async Task SetupAsync(
        Application application,
        MainThreadScheduler scheduler,
        ClientFrameMetrics clientFrameMetrics,
        Dictionary<string, byte[]>? hotReloadData = null)
    {
        await SetupGate.Task;
        _started = true;
    }

    public void Setup(
        Application application,
        MainThreadScheduler scheduler,
        ClientFrameMetrics clientFrameMetrics,
        Dictionary<string, byte[]>? hotReloadData = null) =>
        throw new NotSupportedException();

    public void RegisterTypes(Type[] types) { }

    public void Run(double dt)
    {
        Assert.True(_started);
    }

    public void RunMainThreadBegin() { }
    public void RunMainThreadFrameBegin() { }
    public void RunGameplayFrame(double dt) { }
    public void RunRender() { }
    public bool IsApplicationRunning => true;
    public void Destroy() => throw new NotSupportedException();
    public async Task DestroyAsync() => await DestroyGate.Task;
    public Dictionary<string, byte[]> GetHotReloadData() => [];
    public EditorRuntimeSnapshot CaptureEditorSnapshot() => new();
}

[CollectionDefinition(nameof(WorkingDirectoryCollection), DisableParallelization = true)]
public sealed class WorkingDirectoryCollection;
