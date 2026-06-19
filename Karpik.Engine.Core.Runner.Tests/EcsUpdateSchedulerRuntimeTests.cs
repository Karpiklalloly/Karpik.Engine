using System.Collections.Concurrent;
using Karpik.Engine.Core;
using Karpik.Engine.Shared.ECS.Scheduling;
using Xunit;

public sealed class EcsUpdateSchedulerRuntimeTests
{
    [Fact]
    public async Task ParallelMode_DisjointSystems_StartsBothBeforeEitherCompletes()
    {
        using var bothStarted = new CountdownEvent(2);
        using var release = new ManualResetEventSlim(false);
        var first = new FirstBlockingUpdateSystem(bothStarted, release);
        var second = new SecondBlockingUpdateSystem(bothStarted, release);

        using var scheduler = new EcsUpdateScheduler();
        scheduler.Initialize(
            [first, second],
            [
                Descriptor<FirstBlockingUpdateSystem>(),
                Descriptor<SecondBlockingUpdateSystem>()
            ],
            workerCount: 2);

        Task updateTask = Task.Run(scheduler.Update);

        Assert.True(bothStarted.Wait(TimeSpan.FromSeconds(2)));
        release.Set();
        Assert.Same(updateTask, await Task.WhenAny(updateTask, Task.Delay(TimeSpan.FromSeconds(2))));
        await updateTask;
    }

    [Fact]
    public async Task ParallelMode_ConflictingSystems_SerializesDependentUpdate()
    {
        using var firstEntered = new ManualResetEventSlim(false);
        using var releaseFirst = new ManualResetEventSlim(false);
        var completion = new CompletionFlag();
        var first = new FirstConflictingSystem(firstEntered, releaseFirst, completion);
        var second = new SecondConflictingSystem(completion);

        using var scheduler = new EcsUpdateScheduler();
        scheduler.Initialize(
            [first, second],
            [
                Descriptor<FirstConflictingSystem>(new EcsComponentAccessDescriptor(typeof(RuntimeComponent), EcsAccessMode.Write)),
                Descriptor<SecondConflictingSystem>(new EcsComponentAccessDescriptor(typeof(RuntimeComponent), EcsAccessMode.Read))
            ],
            workerCount: 2);

        Task updateTask = Task.Run(scheduler.Update);

        Assert.True(firstEntered.Wait(TimeSpan.FromSeconds(2)));
        Assert.NotSame(updateTask, await Task.WhenAny(updateTask, Task.Delay(millisecondsDelay: 50)));
        releaseFirst.Set();
        Assert.Same(updateTask, await Task.WhenAny(updateTask, Task.Delay(TimeSpan.FromSeconds(2))));
        await updateTask;
    }

    [Fact]
    public void DeterministicMode_UsesStableGraphExecutionOrder()
    {
        var log = new ConcurrentQueue<string>();
        var predecessor = new PredecessorOrderedSystem(log);
        var dependent = new DependentOrderedSystem(log);

        using var scheduler = new EcsUpdateScheduler();
        scheduler.Initialize(
            [dependent, predecessor],
            [
                Descriptor<DependentOrderedSystem>(
                    order: new EcsSystemOrderDescriptor(typeof(PredecessorOrderedSystem), EcsOrderKind.After)),
                Descriptor<PredecessorOrderedSystem>()
            ],
            EcsUpdateSchedulerMode.Deterministic);

        scheduler.Update();

        Assert.Equal(["predecessor", "dependent"], log.ToArray());
    }

    [Fact]
    public void ParallelMode_AfterWarmup_DoesNotAllocateOnCallingThread()
    {
        var system = new CountingUpdateSystem();
        using var scheduler = new EcsUpdateScheduler();
        scheduler.Initialize([system], [Descriptor<CountingUpdateSystem>()], workerCount: 1);
        scheduler.Update();

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        long before = GC.GetAllocatedBytesForCurrentThread();
        scheduler.Update();
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(0, allocated);
        Assert.Equal(2, system.Count);
    }

    private static EcsUpdateSystemDescriptor Descriptor<TSystem>(
        EcsComponentAccessDescriptor access = default,
        EcsSystemOrderDescriptor order = default)
    {
        EcsComponentAccessDescriptor[] accesses = access.ComponentType is null ? [] : [access];
        EcsSystemOrderDescriptor[] orders = order.TargetSystemType is null ? [] : [order];
        return new EcsUpdateSystemDescriptor(
            typeof(TSystem),
            IsSequential: false,
            accesses,
            orders);
    }

    internal abstract class BlockingUpdateSystem : ISystemUpdate
    {
        private readonly CountdownEvent _bothStarted;
        private readonly ManualResetEventSlim _release;

        protected BlockingUpdateSystem(CountdownEvent bothStarted, ManualResetEventSlim release)
        {
            _bothStarted = bothStarted;
            _release = release;
        }

        public void Update()
        {
            _bothStarted.Signal();
            if (!_release.Wait(TimeSpan.FromSeconds(5)))
            {
                throw new TimeoutException("Parallel update release was not signaled.");
            }
        }
    }

    internal sealed class FirstBlockingUpdateSystem : BlockingUpdateSystem
    {
        public FirstBlockingUpdateSystem(CountdownEvent bothStarted, ManualResetEventSlim release)
            : base(bothStarted, release)
        {
        }
    }

    internal sealed class SecondBlockingUpdateSystem : BlockingUpdateSystem
    {
        public SecondBlockingUpdateSystem(CountdownEvent bothStarted, ManualResetEventSlim release)
            : base(bothStarted, release)
        {
        }
    }

    internal sealed class CompletionFlag
    {
        public int Value;
    }

    internal sealed class FirstConflictingSystem : ISystemUpdate
    {
        private readonly ManualResetEventSlim _entered;
        private readonly ManualResetEventSlim _release;
        private readonly CompletionFlag _completion;

        public FirstConflictingSystem(
            ManualResetEventSlim entered,
            ManualResetEventSlim release,
            CompletionFlag completion)
        {
            _entered = entered;
            _release = release;
            _completion = completion;
        }

        public void Update()
        {
            _entered.Set();
            if (!_release.Wait(TimeSpan.FromSeconds(5)))
            {
                throw new TimeoutException("First conflicting update was not released.");
            }

            Volatile.Write(ref _completion.Value, 1);
        }
    }

    internal sealed class SecondConflictingSystem : ISystemUpdate
    {
        private readonly CompletionFlag _completion;

        public SecondConflictingSystem(CompletionFlag completion)
        {
            _completion = completion;
        }

        public void Update()
        {
            if (Volatile.Read(ref _completion.Value) == 0)
            {
                throw new InvalidOperationException("Dependent update ran before its write dependency.");
            }
        }
    }

    internal abstract class OrderedUpdateSystem : ISystemUpdate
    {
        private readonly string _name;
        private readonly ConcurrentQueue<string> _log;

        protected OrderedUpdateSystem(string name, ConcurrentQueue<string> log)
        {
            _name = name;
            _log = log;
        }

        public void Update()
        {
            _log.Enqueue(_name);
        }
    }

    internal sealed class DependentOrderedSystem : OrderedUpdateSystem
    {
        public DependentOrderedSystem(ConcurrentQueue<string> log)
            : base("dependent", log)
        {
        }
    }

    internal sealed class PredecessorOrderedSystem : OrderedUpdateSystem
    {
        public PredecessorOrderedSystem(ConcurrentQueue<string> log)
            : base("predecessor", log)
        {
        }
    }

    internal sealed class CountingUpdateSystem : ISystemUpdate
    {
        public int Count;

        public void Update()
        {
            Count++;
        }
    }

    private readonly struct RuntimeComponent;
}
