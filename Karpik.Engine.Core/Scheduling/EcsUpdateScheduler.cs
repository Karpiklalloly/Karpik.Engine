using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using Karpik.Engine.Core;
using Karpik.Jobs;

namespace Karpik.Engine.Shared.ECS.Scheduling;

public sealed class EcsUpdateScheduler : IDisposable
{
    private const int JobPayloadByteLength = 64;

    private ISystemUpdate[] _systems = [];
    private EcsUpdateGraph _graph = EcsUpdateGraph.Empty;
    private JobScheduler? _jobScheduler;
    private ValueJobHandle[] _handles = [];
    private ValueJobHandle[] _dependencyBuffer = [];
    private int[] _dependencyOffsets = [];
    private GCHandle _systemsHandle;
    private IntPtr _systemsHandlePointer;
    private EcsUpdateSchedulerMode _mode;
    private int _nextWorkerIndex;
    private bool _isInitialized;
    private bool _isDisposed;

    public EcsUpdateGraph Graph => _graph;

    public void Initialize(
        ReadOnlySpan<ISystemUpdate> systems,
        ReadOnlySpan<EcsUpdateSystemDescriptor> descriptors,
        EcsUpdateSchedulerMode mode = EcsUpdateSchedulerMode.Parallel,
        int workerCount = -1)
    {
        ThrowIfDisposed();
        DisposeSchedulerState();

        _mode = mode;
        _systems = systems.ToArray();
        Type[] systemTypes = new Type[_systems.Length];
        for (int i = 0; i < _systems.Length; i++)
        {
            systemTypes[i] = _systems[i].GetType();
        }

        _graph = EcsUpdateGraphBuilder.Build(systemTypes, descriptors);
        _handles = new ValueJobHandle[_systems.Length];
        _dependencyOffsets = new int[_systems.Length];

        int maxDependencyCount = 0;
        int totalDependencyCount = 0;
        ReadOnlySpan<EcsUpdateGraphNode> nodes = _graph.Nodes.Span;
        for (int i = 0; i < nodes.Length; i++)
        {
            int dependencyCount = nodes[i].DependencySystemIds.Length;
            _dependencyOffsets[i] = totalDependencyCount;
            totalDependencyCount += dependencyCount;
            if (dependencyCount > maxDependencyCount)
            {
                maxDependencyCount = dependencyCount;
            }
        }

        _dependencyBuffer = totalDependencyCount == 0
            ? []
            : new ValueJobHandle[totalDependencyCount];

        if (_systems.Length > 0 && _mode == EcsUpdateSchedulerMode.Parallel)
        {
            int resolvedWorkerCount = workerCount > 0
                ? workerCount
                : Math.Max(1, Math.Min(Environment.ProcessorCount, _systems.Length));

            _systemsHandle = GCHandle.Alloc(_systems, GCHandleType.Normal);
            _systemsHandlePointer = GCHandle.ToIntPtr(_systemsHandle);
            _jobScheduler = new JobScheduler(
                capacity: _systems.Length,
                maxPayloadByteLength: JobPayloadByteLength,
                maxDependenciesPerJob: maxDependencyCount,
                workerCount: resolvedWorkerCount,
                workerQueueCapacity: RoundUpPowerOfTwo(Math.Max(1, _systems.Length)));
            _jobScheduler.StartWorkers();
        }

        _isInitialized = true;
    }

    public void Update()
    {
        if (!_isInitialized || _systems.Length == 0)
        {
            return;
        }

        switch (_mode)
        {
            case EcsUpdateSchedulerMode.SingleThread:
                RunSingleThread();
                break;
            case EcsUpdateSchedulerMode.Deterministic:
                RunDeterministic();
                break;
            case EcsUpdateSchedulerMode.Parallel:
                RunParallel();
                break;
            default:
                throw new InvalidOperationException($"Unsupported ECS update scheduler mode '{_mode}'.");
        }
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        DisposeSchedulerState();
        _isDisposed = true;
    }

    private void RunSingleThread()
    {
        for (int i = 0; i < _systems.Length; i++)
        {
            _systems[i].Update();
        }
    }

    private void RunDeterministic()
    {
        ReadOnlySpan<int> executionOrder = _graph.ExecutionOrder.Span;
        for (int i = 0; i < executionOrder.Length; i++)
        {
            _systems[executionOrder[i]].Update();
        }
    }

    private void RunParallel()
    {
        JobScheduler scheduler = _jobScheduler ??
                                 throw new InvalidOperationException("ECS update scheduler is not initialized.");
        ReadOnlySpan<EcsUpdateGraphNode> nodes = _graph.Nodes.Span;
        ReadOnlySpan<int> executionOrder = _graph.ExecutionOrder.Span;

        for (int orderIndex = 0; orderIndex < executionOrder.Length; orderIndex++)
        {
            int systemId = executionOrder[orderIndex];
            EcsUpdateGraphNode node = nodes[systemId];
            ReadOnlySpan<int> dependencySystemIds = node.DependencySystemIds.Span;
            Span<ValueJobHandle> dependencies = _dependencyBuffer.AsSpan(
                _dependencyOffsets[systemId],
                dependencySystemIds.Length);

            for (int dependencyIndex = 0; dependencyIndex < dependencySystemIds.Length; dependencyIndex++)
            {
                dependencies[dependencyIndex] = _handles[dependencySystemIds[dependencyIndex]];
            }

            var job = new EcsUpdateSystemJob(_systemsHandlePointer, systemId);
            ValueJobHandle handle = scheduler.Schedule(in job, dependencies);
            _handles[systemId] = handle;

            int workerIndex = _nextWorkerIndex;
            _nextWorkerIndex = (_nextWorkerIndex + 1) % scheduler.WorkerCount;
            if (!scheduler.TryPublish(handle, workerIndex))
            {
                throw new InvalidOperationException("Unable to publish ECS update job to worker queue.");
            }
        }

        WaitForScheduledJobs(scheduler);
    }

    private void WaitForScheduledJobs(JobScheduler scheduler)
    {
        SpinWait spinWait = default;
        while (scheduler.ScheduledCount != 0)
        {
            spinWait.SpinOnce();
        }

        for (int i = 0; i < _handles.Length; i++)
        {
            ValueJobHandle handle = _handles[i];
            if (!handle.IsValid || !scheduler.HasException(handle))
            {
                continue;
            }

            Exception? exception = scheduler.GetException(handle);
            if (exception is not null)
            {
                ExceptionDispatchInfo.Capture(exception).Throw();
            }

            throw new InvalidOperationException(
                $"ECS update system '{_systems[i].GetType().FullName}' failed without an exception payload.");
        }
    }

    private void DisposeSchedulerState()
    {
        _jobScheduler?.Dispose();
        _jobScheduler = null;

        if (_systemsHandle.IsAllocated)
        {
            _systemsHandle.Free();
        }

        _systemsHandlePointer = IntPtr.Zero;
        _systems = [];
        _graph = EcsUpdateGraph.Empty;
        _handles = [];
        _dependencyBuffer = [];
        _dependencyOffsets = [];
        _isInitialized = false;
        _nextWorkerIndex = 0;
    }

    private void ThrowIfDisposed()
    {
        if (_isDisposed)
        {
            throw new ObjectDisposedException(nameof(EcsUpdateScheduler));
        }
    }

    private static int RoundUpPowerOfTwo(int value)
    {
        int result = 1;
        while (result < value)
        {
            result <<= 1;
        }

        return result;
    }

    private readonly struct EcsUpdateSystemJob : IJob
    {
        private readonly IntPtr _systemsHandlePointer;
        private readonly int _systemId;

        public EcsUpdateSystemJob(IntPtr systemsHandlePointer, int systemId)
        {
            _systemsHandlePointer = systemsHandlePointer;
            _systemId = systemId;
        }

        public void Execute()
        {
            var systems = (ISystemUpdate[])GCHandle.FromIntPtr(_systemsHandlePointer).Target!;
            systems[_systemId].Update();
        }
    }
}
