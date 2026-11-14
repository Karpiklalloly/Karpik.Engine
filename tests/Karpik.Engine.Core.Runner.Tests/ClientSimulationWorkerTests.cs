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

public sealed class ClientSimulationWorkerMaintenanceTests
{
    [Fact]
    public async Task Maintenance_HoldsReservationAndDefersQueuedWork_UntilReleased()
    {
        using var worker = new ClientSimulationWorker(new NoopLoop());
        Assert.True(worker.TryEnterMaintenance(out var lease));
        Assert.NotNull(lease);
        try
        {
            Assert.False(worker.TryReserveFrame());
            Assert.Throws<InvalidOperationException>(() => worker.StartReservedFrame(Application.TICK_DT));
            Assert.Throws<InvalidOperationException>(worker.CancelReservedFrame);
            Assert.False(worker.TryEnterMaintenance(out var second));
            Assert.Null(second);
            Task<int> queued = worker.InvokeAsync(() => 42);
            Assert.False(queued.IsCompleted);

            lease.Dispose();
            Assert.Equal(42, await queued.WaitAsync(TimeSpan.FromSeconds(2)));
        }
        finally { lease.Dispose(); }
    }

    [Fact]
    public void Maintenance_DoesNotStealReservedButUnstartedFrame()
    {
        using var worker = new ClientSimulationWorker(new NoopLoop());
        Assert.True(worker.TryReserveFrame());
        Assert.False(worker.TryEnterMaintenance(out var rejected));
        Assert.Null(rejected);
        worker.CancelReservedFrame();
        Assert.True(worker.TryEnterMaintenance(out var lease));
        lease!.Dispose();
    }

    [Fact]
    public void Maintenance_DoesNotStealRunningFrame()
    {
        using var loop = new GatedLoop();
        using var worker = new ClientSimulationWorker(loop);
        Assert.True(worker.TryReserveFrame());
        worker.StartReservedFrame(Application.TICK_DT);
        try
        {
            Assert.True(loop.Started.Wait(TimeSpan.FromSeconds(2)));
            Assert.False(worker.TryEnterMaintenance(out var lease));
            Assert.Null(lease);
        }
        finally { loop.Finish.Set(); }
    }

    [Fact]
    public void Lease_DisposeTwice_DoesNotReleaseLaterReservation()
    {
        using var worker = new ClientSimulationWorker(new NoopLoop());
        Assert.True(worker.TryEnterMaintenance(out var lease));
        lease!.Dispose();
        Assert.True(worker.TryReserveFrame());
        try
        {
            lease.Dispose();
            Assert.False(worker.TryReserveFrame());
        }
        finally { worker.CancelReservedFrame(); }
    }

    [Fact]
    public void Maintenance_AfterShutdown_Throws()
    {
        var worker = new ClientSimulationWorker(new NoopLoop());
        worker.Dispose();
        Assert.Throws<ObjectDisposedException>(() => worker.TryEnterMaintenance(out _));
    }

    [Fact]
    public async Task Maintenance_AfterFrameFault_ThrowsOriginalFailure()
    {
        var worker = new ClientSimulationWorker(new FaultingLoop());
        Assert.True(worker.TryReserveFrame());
        worker.StartReservedFrame(Application.TICK_DT);
        await worker.InvokeAsync(() => true).WaitAsync(TimeSpan.FromSeconds(2));
        var error = Assert.Throws<InvalidOperationException>(() => worker.TryEnterMaintenance(out _));
        Assert.Equal("frame fault", error.Message);
        Assert.Throws<InvalidOperationException>(worker.Dispose);
    }

    [Fact]
    public void Dispose_OnMaintenanceOwner_RejectsInvalidOrderingWithoutStoppingWorker()
    {
        var worker = new ClientSimulationWorker(new NoopLoop());
        Assert.True(worker.TryEnterMaintenance(out var lease));
        try
        {
            Assert.Throws<InvalidOperationException>(worker.Dispose);
            Assert.False(worker.TryReserveFrame());
        }
        finally
        {
            lease!.Dispose();
            worker.Dispose();
        }
    }

    [Fact]
    public async Task Dispose_OnAnotherThread_WaitsForMaintenanceRelease()
    {
        var worker = new ClientSimulationWorker(new NoopLoop());
        Assert.True(worker.TryEnterMaintenance(out var lease));
        Task shutdown = Task.Run(worker.Dispose);
        try
        {
            Assert.True(SpinWait.SpinUntil(() =>
            {
                try
                {
                    worker.TryEnterMaintenance(out _);
                    return false;
                }
                catch (ObjectDisposedException) { return true; }
            }, TimeSpan.FromSeconds(2)));
            Assert.False(shutdown.IsCompleted);
            Assert.Throws<ObjectDisposedException>(() => { _ = worker.InvokeAsync(() => true); });
        }
        finally { lease!.Dispose(); }
        await shutdown.WaitAsync(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task Dispose_FromWorkerThread_RejectsSelfJoin()
    {
        using var worker = new ClientSimulationWorker(new NoopLoop());
        await worker.InvokeAsync(() =>
        {
            Assert.Throws<InvalidOperationException>(worker.Dispose);
            return true;
        }).WaitAsync(TimeSpan.FromSeconds(2));
    }

    private sealed class NoopLoop : IClientSimulationLoop
    {
        public bool IsApplicationRunning => true;
        public void RunGameplayFrame(double deltaTime) { }
    }

    private sealed class FaultingLoop : IClientSimulationLoop
    {
        public bool IsApplicationRunning => true;
        public void RunGameplayFrame(double deltaTime) => throw new InvalidOperationException("frame fault");
    }

    private sealed class GatedLoop : IClientSimulationLoop, IDisposable
    {
        public ManualResetEventSlim Started { get; } = new(false);
        public ManualResetEventSlim Finish { get; } = new(false);
        public bool IsApplicationRunning => true;
        public void RunGameplayFrame(double deltaTime)
        {
            Started.Set();
            Finish.Wait();
        }
        public void Dispose() { Started.Dispose(); Finish.Dispose(); }
    }
}
