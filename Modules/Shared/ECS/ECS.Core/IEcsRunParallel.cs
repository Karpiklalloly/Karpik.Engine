using DCFApixels.DragonECS.Core;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using Karpik.Jobs;

namespace Karpik.Engine.Shared.ECS;

public interface IEcsRunParallel : IEcsProcess
{
    void RunParallel();
}

public class EcsRunParallelRunner : EcsRunner<IEcsRunParallel>, IEcsRunParallel, IEcsDestroy
{
    private SystemExecutionNode[] _executionNodes = [];
    private JobScheduler? _jobScheduler;
    private ValueJobHandle[] _jobHandles = [];
    private bool[] _published = [];
    private Exception?[] _exceptions = [];
    private GCHandle _runnerHandle;

    // The caller owns scheduling and teardown; workers only execute the reserved batch.
    public void RunParallel()
    {
        JobScheduler? scheduler = _jobScheduler;
        if (scheduler is null) return;

        Array.Clear(_exceptions);
        Array.Clear(_published);
        IntPtr runnerPointer = GCHandle.ToIntPtr(_runnerHandle);
        // Reserve the entire graph before workers can recycle descriptor generations.
        for (int nodeIndex = 0; nodeIndex < _executionNodes.Length; nodeIndex++)
        {
            var job = new SystemJob(runnerPointer, nodeIndex);
            _jobHandles[nodeIndex] = scheduler.Schedule(in job);
        }

        int remaining = _jobHandles.Length;
        SpinWait spin = default;
        while (remaining != 0)
        {
            bool madeProgress = false;
            for (int i = 0; i < _jobHandles.Length; i++)
            {
                if (_published[i]) continue;
                bool ready = true;
                var dependencies = _executionNodes[i].Dependencies;
                for (int j = 0; j < dependencies.Count; j++)
                {
                    if (!scheduler.IsCompleted(_jobHandles[dependencies[j].Index]))
                    {
                        ready = false;
                        break;
                    }
                }
                if (!ready) continue;

                // Workers must never requeue dependency-blocked jobs: queue bottom
                // publication belongs exclusively to this orchestration thread.
                if (!scheduler.TryPublish(_jobHandles[i], i % scheduler.WorkerCount))
                {
                    DrainUnpublished(scheduler);
                    throw new InvalidOperationException("Unable to publish ECS parallel job to worker queue; batch drained.");
                }
                _published[i] = true;
                remaining--;
                madeProgress = true;
            }
            if (!madeProgress) spin.SpinOnce();
        }

        WaitForCompletion(scheduler);
        for (int i = 0; i < _exceptions.Length; i++)
        {
            if (_exceptions[i] is { } exception)
                ExceptionDispatchInfo.Capture(exception).Throw();
        }
    }

    private void DrainUnpublished(JobScheduler scheduler)
    {
        SpinWait spin = default;
        for (int i = 0; i < _jobHandles.Length; i++)
            if (_published[i])
                while (!scheduler.IsCompleted(_jobHandles[i])) spin.SpinOnce();

        // The original graph only depends on earlier registration indices.
        for (int i = 0; i < _jobHandles.Length; i++)
            if (!_published[i]) scheduler.Complete(_jobHandles[i]);
        WaitForCompletion(scheduler);
    }

    private static void WaitForCompletion(JobScheduler scheduler)
    {
        SpinWait spin = default;
        while (scheduler.ScheduledCount != 0) spin.SpinOnce();
    }

    private readonly struct SystemJob(IntPtr runnerPointer, int nodeIndex) : IJob
    {
        public void Execute()
        {
            var runner = (EcsRunParallelRunner)GCHandle.FromIntPtr(runnerPointer).Target!;
            try { runner._executionNodes[nodeIndex].System.RunParallel(); }
            catch (Exception exception) { runner._exceptions[nodeIndex] = exception; }
        }
    }

    internal static SystemExecutionNode[] CreateDependencyGraph(IReadOnlyList<IEcsRunParallel> systems)
    {
        var nodes = new SystemExecutionNode[systems.Count];
        for (int i = 0; i < nodes.Length; i++)
        {
            nodes[i] = new SystemExecutionNode(i, systems[i]);
        }

        return AddDependencies(nodes);
    }

    private static SystemExecutionNode[] CreateDependencyGraph(EcsProcess<IEcsRunParallel> systems)
    {
        var nodes = new SystemExecutionNode[systems.Length];
        for (int i = 0; i < nodes.Length; i++)
        {
            nodes[i] = new SystemExecutionNode(i, systems[i]);
        }

        return AddDependencies(nodes);
    }

    private static SystemExecutionNode[] AddDependencies(SystemExecutionNode[] nodes)
    {
        for (int i = 0; i < nodes.Length; i++)
        {
            var nodeA = nodes[i];

            for (int j = i + 1; j < nodes.Length; j++)
            {
                var nodeB = nodes[j];
                if (HasDirectedConflict(nodeA, nodeB))
                {
                    nodeB.AddDependency(nodeA);
                }
            }
        }

        return nodes;
    }
    
    private static bool HasDirectedConflict(SystemExecutionNode previousNode, SystemExecutionNode subsequentNode)
    {
        if (!previousNode.IsAccessKnown || !subsequentNode.IsAccessKnown)
        {
            return true;
        }

        var writesOfPrevious = previousNode.WriteTypes;
    
        var subsequentReadsOrWrites = new HashSet<Type>(subsequentNode.ReadTypes);
        subsequentReadsOrWrites.UnionWith(subsequentNode.WriteTypes);

        if (writesOfPrevious.Overlaps(subsequentReadsOrWrites))
        {
            return true;
        }

        return previousNode.ReadTypes.Overlaps(subsequentNode.WriteTypes);
    }

    public void Init()
    {
        Destroy();
        _executionNodes = CreateDependencyGraph(Process);
        int count = _executionNodes.Length;
        if (count == 0) return;

        _jobHandles = new ValueJobHandle[count];
        _published = new bool[count];
        _exceptions = new Exception?[count];

        int queueCapacity = 1;
        while (queueCapacity < count) queueCapacity <<= 1;
        _jobScheduler = new JobScheduler(count, maxPayloadByteLength: 16,
            maxDependenciesPerJob: 0,
            workerCount: Math.Max(1, Math.Min(Environment.ProcessorCount, count)),
            workerQueueCapacity: queueCapacity);
        _runnerHandle = GCHandle.Alloc(this);
        _jobScheduler.StartWorkers();
    }

    public void Destroy()
    {
        if (_jobScheduler is { } scheduler)
        {
            WaitForCompletion(scheduler);
            scheduler.Dispose();
            _jobScheduler = null;
        }
        if (_runnerHandle.IsAllocated) _runnerHandle.Free();
        foreach (var node in _executionNodes) node.Destroy();
        _executionNodes = [];
        _jobHandles = [];
        _published = [];
        _exceptions = [];
    }
}
