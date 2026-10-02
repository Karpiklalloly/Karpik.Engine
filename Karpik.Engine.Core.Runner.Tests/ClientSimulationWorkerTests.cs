using Karpik.Engine.Core;
using Xunit;

public sealed class ClientSimulationWorkerTests
{
    [Fact]
    public void TryReserveFrame_WhileFrameIsRunning_ReturnsFalseWithoutWaiting()
    {
        using var loop = new BlockingSimulationLoop();
        using var worker = new ClientSimulationWorker(loop);

        Assert.True(worker.TryReserveFrame());
        worker.StartReservedFrame(Application.TICK_DT);
        Assert.True(loop.FrameStarted.Wait(TimeSpan.FromSeconds(1)));

        Assert.False(worker.TryReserveFrame());

        loop.AllowFrameCompletion.Set();
    }

    [Fact]
    public async Task Dispose_WaitsForReservedFrameToComplete()
    {
        using var loop = new BlockingSimulationLoop();
        var worker = new ClientSimulationWorker(loop);

        Assert.True(worker.TryReserveFrame());
        worker.StartReservedFrame(Application.TICK_DT);
        Assert.True(loop.FrameStarted.Wait(TimeSpan.FromSeconds(1)));

        Task dispose = Task.Run(worker.Dispose);
        Task completed = await Task.WhenAny(dispose, Task.Delay(TimeSpan.FromMilliseconds(50)));
        Assert.NotSame(dispose, completed);

        loop.AllowFrameCompletion.Set();
        await dispose.WaitAsync(TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void ThrowIfFaulted_PropagatesWorkerFrameException()
    {
        using var loop = new ThrowingSimulationLoop();
        var worker = new ClientSimulationWorker(loop);

        Assert.True(worker.TryReserveFrame());
        worker.StartReservedFrame(Application.TICK_DT);
        Assert.True(loop.FrameAttempted.Wait(TimeSpan.FromSeconds(1)));
        InvalidOperationException? exception = null;
        bool faultPublished = SpinWait.SpinUntil(() =>
        {
            try
            {
                worker.ThrowIfFaulted();
                return false;
            }
            catch (InvalidOperationException caught)
            {
                exception = caught;
                return true;
            }
        }, TimeSpan.FromSeconds(1));

        Assert.True(faultPublished);
        Assert.NotNull(exception);
        Assert.Equal(ThrowingSimulationLoop.Message, exception.Message);
        Assert.Throws<InvalidOperationException>(worker.Dispose);
    }

    [Fact]
    public async Task InvokeAsync_WithoutReservedFrame_ExecutesOnSimulationThread()
    {
        using var loop = new BlockingSimulationLoop();
        using var worker = new ClientSimulationWorker(loop);
        int callerThreadId = Environment.CurrentManagedThreadId;

        int executionThreadId = await worker.InvokeAsync(
            () => Environment.CurrentManagedThreadId);

        Assert.NotEqual(callerThreadId, executionThreadId);
    }

    [Fact]
    public async Task InvokeAsync_WhileFrameIsReserved_WaitsForFrameCompletion()
    {
        using var loop = new BlockingSimulationLoop();
        using var worker = new ClientSimulationWorker(loop);
        Assert.True(worker.TryReserveFrame());

        try
        {
            Task<int> work = worker.InvokeAsync(() => 42);
            await Task.Delay(50);
            Assert.False(work.IsCompleted);

            worker.StartReservedFrame(Application.TICK_DT);
            Assert.True(loop.FrameStarted.Wait(TimeSpan.FromSeconds(1)));
            Assert.False(work.IsCompleted);

            loop.AllowFrameCompletion.Set();
            Assert.Equal(42, await work.WaitAsync(TimeSpan.FromSeconds(1)));
        }
        finally
        {
            loop.AllowFrameCompletion.Set();
        }
    }

    private sealed class BlockingSimulationLoop : IClientSimulationLoop, IDisposable
    {
        public ManualResetEventSlim FrameStarted { get; } = new(false);
        public ManualResetEventSlim AllowFrameCompletion { get; } = new(false);

        public bool IsApplicationRunning => true;

        public void RunGameplayFrame(double deltaTime)
        {
            FrameStarted.Set();
            AllowFrameCompletion.Wait();
        }

        public void Dispose()
        {
            FrameStarted.Dispose();
            AllowFrameCompletion.Dispose();
        }
    }

    private sealed class ThrowingSimulationLoop : IClientSimulationLoop, IDisposable
    {
        public const string Message = "Test worker failure";
        public ManualResetEventSlim FrameAttempted { get; } = new(false);

        public bool IsApplicationRunning => true;

        public void RunGameplayFrame(double deltaTime)
        {
            FrameAttempted.Set();
            throw new InvalidOperationException(Message);
        }

        public void Dispose()
        {
            FrameAttempted.Dispose();
        }
    }
}
