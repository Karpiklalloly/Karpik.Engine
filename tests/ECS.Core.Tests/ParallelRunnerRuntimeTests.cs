using DCFApixels.DragonECS;
using DragonExtensions;
using Karpik.Engine.Shared.ECS;
using Karpik.Jobs;
using System.Reflection;
using Xunit;

[CollectionDefinition(nameof(ParallelRunnerCollection), DisableParallelization = true)]
public sealed class ParallelRunnerCollection;

[Collection(nameof(ParallelRunnerCollection))]
public sealed class ParallelRunnerRuntimeTests
{
    [Fact]
    public async Task RunParallel_ReadOnlySystems_CanOverlap()
    {
        if (Environment.ProcessorCount < 2) return; // Runtime intentionally uses one worker on single-core hosts.
        using var entered = new CountdownEvent(2);
        using var release = new ManualResetEventSlim();
        var runner = Create(new BlockingReader(entered, release), new BlockingReader(entered, release));
        Task run = Task.Factory.StartNew(runner.RunParallel, CancellationToken.None,
            TaskCreationOptions.LongRunning, TaskScheduler.Default);
        bool overlapped;
        try { overlapped = entered.Wait(TimeSpan.FromSeconds(5)); }
        finally { release.Set(); }
        try
        {
            await run.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.True(overlapped);
        }
        finally { runner.Destroy(); }
    }

    [Fact]
    public void RunParallel_PublicationRejected_DrainsReservedWorkBeforeTeardown()
    {
        var probe = new AllocationProbe();
        var runner = Create(probe);
        // Stop the real scheduler to force publication rejection without modifying production APIs.
        var scheduler = (JobScheduler)typeof(EcsRunParallelRunner)
            .GetField("_jobScheduler", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(runner)!;
        scheduler.StopWorkers();
        try
        {
            Assert.Throws<InvalidOperationException>(runner.RunParallel);
            Assert.Equal(1, probe.Count);
            Assert.Equal(0, scheduler.ScheduledCount);
        }
        finally { runner.Destroy(); }
    }

    [Fact]
    public void RunParallel_AfterWarmup_DoesNotAllocateOnCallingThread()
    {
        var first = new AllocationProbe();
        var second = new AllocationProbe();
        var runner = Create(first, second);
        try
        {
            for (int i = 0; i < 32; i++) runner.RunParallel();
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 64; i++) runner.RunParallel();
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            Assert.Equal(0, allocated);
            Assert.Equal(96, first.Count);
            Assert.Equal(96, second.Count);
        }
        finally { runner.Destroy(); }
    }

    [Fact]
    public void RunParallel_ConflictsAndUnknownAccess_PreserveOrderAcrossFrames()
    {
        int[] order = new int[4];
        var cursor = new Cursor();
        var runner = Create(new Writer(order, cursor, 1), new Reader(order, cursor, 2),
            new Unknown(order, cursor, 3), new Writer(order, cursor, 4));
        try
        {
            for (int frame = 0; frame < 64; frame++)
            {
                cursor.Value = 0;
                runner.RunParallel();
                Assert.Equal(new[] { 1, 2, 3, 4 }, order);
            }
        }
        finally { runner.Destroy(); }
    }

    [Fact]
    public void RunParallel_WorkerFailure_IsReportedAfterOtherWorkCompletes()
    {
        var probe = new AllocationProbe();
        var runner = Create(new Failing(), probe);
        try
        {
            Assert.Throws<ProbeException>(runner.RunParallel);
            Assert.Equal(1, probe.Count);
        }
        finally { runner.Destroy(); }
    }

    [Fact]
    public void RunParallel_EmptyAndRepeatedLifetimes_AreSafe()
    {
        var empty = Create();
        empty.RunParallel();
        empty.Destroy();
        for (int i = 0; i < 8; i++)
        {
            var probe = new AllocationProbe();
            var runner = Create(probe);
            runner.RunParallel();
            runner.Destroy();
            Assert.Equal(1, probe.Count);
            runner.Init();
            runner.RunParallel();
            runner.Destroy();
            Assert.Equal(2, probe.Count);
        }
    }

    private static EcsRunParallelRunner Create(params IEcsRunParallel[] systems)
    {
        var builder = EcsPipeline.New();
        foreach (var system in systems) builder.Add(system);
        var pipeline = builder.AddRunner<EcsRunParallelRunner>().Build();
        var runner = pipeline.GetRunnerInstance<EcsRunParallelRunner>();
        runner.Init();
        return runner;
    }

    private struct Component : IEcsComponent;
    private sealed class Cursor { public int Value; }

    private sealed class AllocationProbe : IEcsRunParallel
    {
        public int Count;
        public void RunParallel()
        {
            Count++;
        }
    }

    private sealed class Writer(int[] order, Cursor cursor, int value) : IEcsRunParallel
    {
        private sealed class Aspect : EcsAspect { public EcsPool<Component> Pool = null!; }
        public void RunParallel() => order[cursor.Value++] = value;
    }

    private sealed class Reader(int[] order, Cursor cursor, int value) : IEcsRunParallel
    {
        private sealed class Aspect : EcsAspect { public ReadonlyEcsPool<Component> Pool = default; }
        public void RunParallel() => order[cursor.Value++] = value;
    }

    private sealed class BlockingReader(CountdownEvent entered, ManualResetEventSlim release) : IEcsRunParallel
    {
        private sealed class Aspect : EcsAspect { public ReadonlyEcsPool<Component> Pool = default; }
        public void RunParallel()
        {
            entered.Signal();
            if (!release.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException();
        }
    }

    private sealed class Unknown(int[] order, Cursor cursor, int value) : IEcsRunParallel
    {
        public void RunParallel() => order[cursor.Value++] = value;
    }

    private sealed class ProbeException : Exception;
    private sealed class Failing : IEcsRunParallel
    {
        public void RunParallel() => throw new ProbeException();
    }
}
