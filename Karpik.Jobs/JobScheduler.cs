using System.Diagnostics;
using System.Runtime.CompilerServices;
using Karpik.Memory;

namespace Karpik.Jobs;

public sealed unsafe class JobScheduler : IDisposable
{
    public const int DefaultPayloadAlignment = 16;

    private const int WorkerRuntimeNotStarted = 0;
    private const int WorkerRuntimeRunning = 1;
    private const int WorkerRuntimeStopped = 2;

    private readonly JobDescriptorPool _descriptors;
    private readonly NativeLinearAllocator _payloadAllocator;
    private readonly NativeMemorySlice _payloadBlock;
    private readonly NativeArray<ValueJobHandle> _dependencies;
    private readonly NativeArray<int> _completedGenerations;
    private readonly NativeArray<int> _failedGenerations;
    private readonly JobProfilerHooks? _profilerHooks;
    private readonly WorkStealingDeque<ValueJobHandle>[] _workerQueues;
    private readonly ManualResetEventSlim _workerWakeEvent;
    private readonly WorkerThreadState[] _workerStates;
    private readonly Thread[] _workerThreads;
    private readonly byte* _payloadBasePointer;
    private readonly int _payloadStride;
    private ValueJobHandle _lastExceptionHandle;
    private Exception? _lastException;
    private int _workerRuntimeState;
    private long _descriptorExhaustionCount;
    private long _payloadTooLargeCount;
    private long _dependencyBudgetExhaustionCount;
    private long _invalidDependencyCount;
    private long _workerQueueOverflowCount;
    private long _requeueOverflowCount;
    private bool _isDisposed;

    public JobScheduler(
        int capacity,
        int maxPayloadByteLength,
        int payloadAlignment = DefaultPayloadAlignment,
        int maxDependenciesPerJob = 4,
        JobProfilerHooks? profilerHooks = null,
        int workerCount = 1,
        int workerQueueCapacity = 64)
    {
        if (capacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity), capacity, "Capacity must be greater than zero.");
        }

        if (maxPayloadByteLength <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxPayloadByteLength),
                maxPayloadByteLength,
                "Max payload byte length must be greater than zero.");
        }

        if (payloadAlignment <= 0 || ((uint)payloadAlignment & ((uint)payloadAlignment - 1)) != 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(payloadAlignment),
                payloadAlignment,
                "Payload alignment must be a non-zero power of two.");
        }

        if (maxDependenciesPerJob < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxDependenciesPerJob),
                maxDependenciesPerJob,
                "Max dependencies per job must be greater than or equal to zero.");
        }

        if (workerCount <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(workerCount),
                workerCount,
                "Worker count must be greater than zero.");
        }

        if (workerQueueCapacity <= 0 || (workerQueueCapacity & (workerQueueCapacity - 1)) != 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(workerQueueCapacity),
                workerQueueCapacity,
                "Worker queue capacity must be a positive power of two.");
        }

        Capacity = capacity;
        MaxPayloadByteLength = maxPayloadByteLength;
        PayloadAlignment = payloadAlignment;
        MaxDependenciesPerJob = maxDependenciesPerJob;
        WorkerCount = workerCount;
        WorkerQueueCapacity = workerQueueCapacity;

        _payloadStride = AlignUp(maxPayloadByteLength, payloadAlignment);
        int payloadByteCapacity = checked(capacity * _payloadStride);

        _descriptors = new JobDescriptorPool(capacity);
        _payloadAllocator = new NativeLinearAllocator(payloadByteCapacity, payloadAlignment);
        _payloadBlock = _payloadAllocator.Allocate((nuint)payloadByteCapacity, (nuint)payloadAlignment);
        _dependencies = new NativeArray<ValueJobHandle>(checked(capacity * maxDependenciesPerJob));
        _completedGenerations = new NativeArray<int>(capacity);
        _failedGenerations = new NativeArray<int>(capacity);
        _completedGenerations.Clear();
        _failedGenerations.Clear();
        _payloadBasePointer = (byte*)_payloadBlock.Pointer;
        _profilerHooks = profilerHooks;
        _workerQueues = new WorkStealingDeque<ValueJobHandle>[workerCount];
        for (int i = 0; i < workerCount; i++)
        {
            _workerQueues[i] = new WorkStealingDeque<ValueJobHandle>(workerQueueCapacity);
        }

        _workerWakeEvent = new ManualResetEventSlim(initialState: false);
        _workerStates = new WorkerThreadState[workerCount];
        _workerThreads = new Thread[workerCount];
    }

    public int Capacity { get; }
    public int MaxPayloadByteLength { get; }
    public int PayloadAlignment { get; }
    public int MaxDependenciesPerJob { get; }
    public int WorkerCount { get; }
    public int WorkerQueueCapacity { get; }
    public int ScheduledCount => _descriptors.RentedCount;
    public int AvailableDescriptorCount => _descriptors.AvailableCount;
    public bool AreWorkersRunning => Volatile.Read(ref _workerRuntimeState) == WorkerRuntimeRunning;

    public ValueJobHandle Schedule<TJob>(in TJob job)
        where TJob : unmanaged, IJob
    {
        if (!TrySchedule(in job, out ValueJobHandle handle))
        {
            throw new InvalidOperationException("Unable to schedule value job with the configured descriptor/payload capacity.");
        }

        return handle;
    }

    public bool TrySchedule<TJob>(in TJob job, out ValueJobHandle handle)
        where TJob : unmanaged, IJob
    {
        return TrySchedule(in job, ReadOnlySpan<ValueJobHandle>.Empty, out handle);
    }

    public ValueJobHandle Schedule<TJob>(in TJob job, ReadOnlySpan<ValueJobHandle> dependencies)
        where TJob : unmanaged, IJob
    {
        if (!TrySchedule(in job, dependencies, out ValueJobHandle handle))
        {
            throw new InvalidOperationException("Unable to schedule value job with the configured descriptor/payload/dependency capacity.");
        }

        return handle;
    }

    public bool TrySchedule<TJob>(
        in TJob job,
        ReadOnlySpan<ValueJobHandle> dependencies,
        out ValueJobHandle handle)
        where TJob : unmanaged, IJob
    {
        EnsureNotDisposed();

        if (!CanStorePayload<TJob>())
        {
            Interlocked.Increment(ref _payloadTooLargeCount);
            handle = default;
            return false;
        }

        if (!CanStoreDependencies(dependencies))
        {
            handle = default;
            return false;
        }

        if (!_descriptors.TryRent(JobDescriptorKind.Single, out JobDescriptorHandle descriptorHandle))
        {
            Interlocked.Increment(ref _descriptorExhaustionCount);
            handle = default;
            return false;
        }

        void* payloadPointer = PayloadPointer(descriptorHandle.Index);
        *(TJob*)payloadPointer = job;

        ref JobDescriptor descriptor = ref _descriptors.Get(descriptorHandle);
        descriptor.PayloadPointer = payloadPointer;
        descriptor.PayloadByteOffset = descriptorHandle.Index * _payloadStride;
        descriptor.PayloadByteLength = Unsafe.SizeOf<TJob>();
        descriptor.Execute = &JobExecutor<TJob>.Execute;
        descriptor.ExecuteFor = null;
        StoreDependencies(ref descriptor, descriptorHandle.Index, dependencies);

        handle = new ValueJobHandle(descriptorHandle);
        EmitPublished(handle, ref descriptor);
        return true;
    }

    public ValueJobHandle ScheduleParallel<TJob>(in TJob job, int length, int batchSize)
        where TJob : unmanaged, IJobFor
    {
        if (!TryScheduleParallel(in job, length, batchSize, out ValueJobHandle handle))
        {
            throw new InvalidOperationException("Unable to schedule value parallel job with the configured descriptor/payload capacity.");
        }

        return handle;
    }

    public bool TryScheduleParallel<TJob>(
        in TJob job,
        int length,
        int batchSize,
        out ValueJobHandle handle)
        where TJob : unmanaged, IJobFor
    {
        return TryScheduleParallel(in job, length, batchSize, ReadOnlySpan<ValueJobHandle>.Empty, out handle);
    }

    public ValueJobHandle ScheduleParallel<TJob>(
        in TJob job,
        int length,
        int batchSize,
        ReadOnlySpan<ValueJobHandle> dependencies)
        where TJob : unmanaged, IJobFor
    {
        if (!TryScheduleParallel(in job, length, batchSize, dependencies, out ValueJobHandle handle))
        {
            throw new InvalidOperationException("Unable to schedule value parallel job with the configured descriptor/payload/dependency capacity.");
        }

        return handle;
    }

    public bool TryScheduleParallel<TJob>(
        in TJob job,
        int length,
        int batchSize,
        ReadOnlySpan<ValueJobHandle> dependencies,
        out ValueJobHandle handle)
        where TJob : unmanaged, IJobFor
    {
        EnsureNotDisposed();

        if (length < 0 || batchSize <= 0)
        {
            handle = default;
            return false;
        }

        if (!CanStorePayload<TJob>())
        {
            Interlocked.Increment(ref _payloadTooLargeCount);
            handle = default;
            return false;
        }

        if (!CanStoreDependencies(dependencies))
        {
            handle = default;
            return false;
        }

        if (!_descriptors.TryRent(JobDescriptorKind.ParallelBatch, out JobDescriptorHandle descriptorHandle))
        {
            Interlocked.Increment(ref _descriptorExhaustionCount);
            handle = default;
            return false;
        }

        void* payloadPointer = PayloadPointer(descriptorHandle.Index);
        *(TJob*)payloadPointer = job;

        ref JobDescriptor descriptor = ref _descriptors.Get(descriptorHandle);
        descriptor.PayloadPointer = payloadPointer;
        descriptor.PayloadByteOffset = descriptorHandle.Index * _payloadStride;
        descriptor.PayloadByteLength = Unsafe.SizeOf<TJob>();
        descriptor.StartIndex = 0;
        descriptor.EndIndex = length;
        descriptor.BatchSize = batchSize;
        descriptor.BatchCount = length == 0 ? 0 : (length + batchSize - 1) / batchSize;
        descriptor.Execute = null;
        descriptor.ExecuteFor = &JobForExecutor<TJob>.Execute;
        StoreDependencies(ref descriptor, descriptorHandle.Index, dependencies);

        handle = new ValueJobHandle(descriptorHandle);
        EmitPublished(handle, ref descriptor);
        return true;
    }

    public void Complete(ValueJobHandle handle)
    {
        if (!TryComplete(handle))
        {
            throw new InvalidOperationException("Value job dependencies are not completed.");
        }
    }

    public bool TryComplete(ValueJobHandle handle)
    {
        EnsureNotDisposed();

        ref JobDescriptor descriptor = ref _descriptors.Get(handle.Descriptor);
        int remainingDependencies = CountRemainingDependencies(ref descriptor);
        descriptor.RemainingDependencies = remainingDependencies;
        if (remainingDependencies != 0)
        {
            return false;
        }

        try
        {
            if (descriptor.Kind == JobDescriptorKind.Single)
            {
                EmitStarted(handle, ref descriptor);
                descriptor.Execute(descriptor.PayloadPointer);
                MarkCompleted(handle);
                EmitCompleted(handle, ref descriptor);
                return true;
            }

            if (descriptor.Kind == JobDescriptorKind.ParallelBatch)
            {
                EmitStarted(handle, ref descriptor);
                for (int i = descriptor.StartIndex; i < descriptor.EndIndex; i++)
                {
                    descriptor.ExecuteFor(descriptor.PayloadPointer, i);
                }

                MarkCompleted(handle);
                EmitCompleted(handle, ref descriptor);
                return true;
            }

            throw new InvalidOperationException($"Unsupported job descriptor kind {descriptor.Kind}.");
        }
        catch (Exception exception)
        {
            MarkFailed(handle, exception);
            EmitFailed(handle, ref descriptor);
            throw;
        }
        finally
        {
            _descriptors.TryReturn(handle.Descriptor);
        }
    }

    public bool IsCompleted(ValueJobHandle handle)
    {
        EnsureNotDisposed();
        return handle.IsValid &&
               (uint)handle.Descriptor.Index < (uint)Capacity &&
               Volatile.Read(ref _completedGenerations[handle.Descriptor.Index]) == handle.Descriptor.Generation;
    }

    public bool HasException(ValueJobHandle handle)
    {
        EnsureNotDisposed();
        return handle.IsValid &&
               (uint)handle.Descriptor.Index < (uint)Capacity &&
               Volatile.Read(ref _failedGenerations[handle.Descriptor.Index]) == handle.Descriptor.Generation;
    }

    public Exception? GetException(ValueJobHandle handle)
    {
        EnsureNotDisposed();
        return HasException(handle) &&
               _lastExceptionHandle.Descriptor.Index == handle.Descriptor.Index &&
               _lastExceptionHandle.Descriptor.Generation == handle.Descriptor.Generation
            ? _lastException
            : null;
    }

    public bool TryGetBatchInfo(ValueJobHandle handle, out JobBatchInfo batchInfo)
    {
        EnsureNotDisposed();

        if (!handle.IsValid ||
            (uint)handle.Descriptor.Index >= (uint)Capacity ||
            !_descriptors.IsRented(handle.Descriptor))
        {
            batchInfo = default;
            return false;
        }

        ref JobDescriptor descriptor = ref _descriptors.Get(handle.Descriptor);
        if (descriptor.Kind != JobDescriptorKind.ParallelBatch)
        {
            batchInfo = default;
            return false;
        }

        batchInfo = CreateBatchInfo(ref descriptor);
        return true;
    }

    public bool TryPublish(ValueJobHandle handle, int workerIndex)
    {
        EnsureNotDisposed();
        if (Volatile.Read(ref _workerRuntimeState) == WorkerRuntimeStopped ||
            !IsValidWorkerIndex(workerIndex) ||
            !handle.IsValid ||
            (uint)handle.Descriptor.Index >= (uint)Capacity ||
            !_descriptors.IsRented(handle.Descriptor))
        {
            return false;
        }

        if (!_workerQueues[workerIndex].TryPushBottom(handle))
        {
            Interlocked.Increment(ref _workerQueueOverflowCount);
            return false;
        }

        if (Volatile.Read(ref _workerRuntimeState) == WorkerRuntimeRunning)
        {
            _workerWakeEvent.Set();
        }

        return true;
    }

    public bool TryRunNext(int workerIndex)
    {
        EnsureNotDisposed();
        if (Volatile.Read(ref _workerRuntimeState) == WorkerRuntimeRunning ||
            !IsValidWorkerIndex(workerIndex))
        {
            return false;
        }

        WorkStealingDeque<ValueJobHandle> localQueue = _workerQueues[workerIndex];
        if (localQueue.TryPopBottom(out ValueJobHandle localHandle))
        {
            return TryCompleteOrRequeue(localHandle, localQueue);
        }

        for (int offset = 1; offset < WorkerCount; offset++)
        {
            int victimIndex = (workerIndex + offset) % WorkerCount;
            WorkStealingDeque<ValueJobHandle> victimQueue = _workerQueues[victimIndex];
            if (victimQueue.TryStealTop(out ValueJobHandle stolenHandle))
            {
                return TryCompleteOrRequeue(stolenHandle, victimQueue);
            }
        }

        return false;
    }

    public int GetWorkerQueueCount(int workerIndex)
    {
        EnsureNotDisposed();
        return IsValidWorkerIndex(workerIndex)
            ? _workerQueues[workerIndex].Count
            : 0;
    }

    public JobRuntimeDiagnostics GetDiagnostics()
    {
        EnsureNotDisposed();
        return new JobRuntimeDiagnostics(
            Volatile.Read(ref _descriptorExhaustionCount),
            Volatile.Read(ref _payloadTooLargeCount),
            Volatile.Read(ref _dependencyBudgetExhaustionCount),
            Volatile.Read(ref _invalidDependencyCount),
            Volatile.Read(ref _workerQueueOverflowCount),
            Volatile.Read(ref _requeueOverflowCount));
    }

    public bool StartWorkers()
    {
        EnsureNotDisposed();
        if (Interlocked.CompareExchange(
                ref _workerRuntimeState,
                WorkerRuntimeRunning,
                WorkerRuntimeNotStarted) != WorkerRuntimeNotStarted)
        {
            return false;
        }

        for (int i = 0; i < WorkerCount; i++)
        {
            WorkerThreadState state = new(this, i);
            Thread thread = new(WorkerLoop)
            {
                IsBackground = true,
                Name = $"Karpik Value Job Worker {i}"
            };

            _workerStates[i] = state;
            _workerThreads[i] = thread;
            thread.Start(state);
        }

        return true;
    }

    public void StopWorkers()
    {
        if (Volatile.Read(ref _workerRuntimeState) != WorkerRuntimeRunning)
        {
            return;
        }

        if (Interlocked.Exchange(ref _workerRuntimeState, WorkerRuntimeStopped) != WorkerRuntimeRunning)
        {
            return;
        }

        _workerWakeEvent.Set();
        for (int i = 0; i < _workerThreads.Length; i++)
        {
            _workerThreads[i]?.Join();
        }
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            throw new ObjectDisposedException(nameof(JobScheduler));
        }

        StopWorkers();
        _isDisposed = true;
        for (int i = 0; i < _workerQueues.Length; i++)
        {
            _workerQueues[i].Dispose();
        }

        _workerWakeEvent.Dispose();
        _failedGenerations.Dispose();
        _completedGenerations.Dispose();
        _dependencies.Dispose();
        _payloadAllocator.Dispose();
        _descriptors.Dispose();
    }

    private bool CanStoreDependencies(ReadOnlySpan<ValueJobHandle> dependencies)
    {
        if (dependencies.Length > MaxDependenciesPerJob)
        {
            Interlocked.Increment(ref _dependencyBudgetExhaustionCount);
            return false;
        }

        for (int i = 0; i < dependencies.Length; i++)
        {
            ValueJobHandle dependency = dependencies[i];
            if (!dependency.IsValid ||
                (uint)dependency.Descriptor.Index >= (uint)Capacity ||
                (!IsCompleted(dependency) && !_descriptors.IsRented(dependency.Descriptor)))
            {
                Interlocked.Increment(ref _invalidDependencyCount);
                return false;
            }
        }

        return true;
    }

    private void StoreDependencies(
        ref JobDescriptor descriptor,
        int descriptorIndex,
        ReadOnlySpan<ValueJobHandle> dependencies)
    {
        int dependencyOffset = descriptorIndex * MaxDependenciesPerJob;
        descriptor.DependencyOffset = dependencyOffset;
        descriptor.DependencyCount = dependencies.Length;
        int remainingDependencies = 0;

        for (int i = 0; i < dependencies.Length; i++)
        {
            ValueJobHandle dependency = dependencies[i];
            _dependencies[dependencyOffset + i] = dependency;
            if (!IsCompleted(dependency))
            {
                remainingDependencies++;
            }
        }

        descriptor.RemainingDependencies = remainingDependencies;
    }

    private int CountRemainingDependencies(ref JobDescriptor descriptor)
    {
        int remainingDependencies = 0;
        int dependencyEnd = descriptor.DependencyOffset + descriptor.DependencyCount;

        for (int i = descriptor.DependencyOffset; i < dependencyEnd; i++)
        {
            if (!IsCompleted(_dependencies[i]))
            {
                remainingDependencies++;
            }
        }

        return remainingDependencies;
    }

    private void MarkCompleted(ValueJobHandle handle)
    {
        Volatile.Write(ref _completedGenerations[handle.Descriptor.Index], handle.Descriptor.Generation);
        if (Volatile.Read(ref _workerRuntimeState) == WorkerRuntimeRunning)
        {
            _workerWakeEvent.Set();
        }
    }

    private void MarkFailed(ValueJobHandle handle, Exception exception)
    {
        MarkCompleted(handle);
        Volatile.Write(ref _failedGenerations[handle.Descriptor.Index], handle.Descriptor.Generation);
        _lastExceptionHandle = handle;
        _lastException = exception;
    }

    private bool TryCompleteOrRequeue(ValueJobHandle handle, WorkStealingDeque<ValueJobHandle> queue)
    {
        if (TryComplete(handle))
        {
            return true;
        }

        if (!queue.TryPushBottom(handle))
        {
            Interlocked.Increment(ref _requeueOverflowCount);
        }

        return false;
    }

    private WorkerRunResult TryRunNextPublished(int workerIndex)
    {
        WorkStealingDeque<ValueJobHandle> localQueue = _workerQueues[workerIndex];
        if (localQueue.TryStealTop(out ValueJobHandle localHandle))
        {
            return TryCompleteOrRequeuePublished(localHandle, localQueue);
        }

        for (int offset = 1; offset < WorkerCount; offset++)
        {
            int victimIndex = (workerIndex + offset) % WorkerCount;
            WorkStealingDeque<ValueJobHandle> victimQueue = _workerQueues[victimIndex];
            if (victimQueue.TryStealTop(out ValueJobHandle stolenHandle))
            {
                return TryCompleteOrRequeuePublished(stolenHandle, victimQueue);
            }
        }

        return WorkerRunResult.Empty;
    }

    private WorkerRunResult TryCompleteOrRequeuePublished(
        ValueJobHandle handle,
        WorkStealingDeque<ValueJobHandle> queue)
    {
        if (TryComplete(handle))
        {
            return WorkerRunResult.Completed;
        }

        if (!queue.TryPushBottom(handle))
        {
            Interlocked.Increment(ref _requeueOverflowCount);
        }

        return WorkerRunResult.PendingDependency;
    }

    private void RunWorkerLoop(int workerIndex)
    {
        while (Volatile.Read(ref _workerRuntimeState) == WorkerRuntimeRunning)
        {
            WorkerRunResult result;
            try
            {
                result = TryRunNextPublished(workerIndex);
            }
            catch
            {
                result = WorkerRunResult.Completed;
            }

            if (result == WorkerRunResult.Completed)
            {
                continue;
            }

            _workerWakeEvent.Reset();
            if (Volatile.Read(ref _workerRuntimeState) != WorkerRuntimeRunning)
            {
                break;
            }

            if (result == WorkerRunResult.Empty && HasQueuedWorkerWork())
            {
                continue;
            }

            _workerWakeEvent.Wait(millisecondsTimeout: result == WorkerRunResult.PendingDependency ? 1 : -1);
        }
    }

    private bool HasQueuedWorkerWork()
    {
        for (int i = 0; i < _workerQueues.Length; i++)
        {
            if (_workerQueues[i].Count != 0)
            {
                return true;
            }
        }

        return false;
    }

    private static void WorkerLoop(object? state)
    {
        WorkerThreadState workerState = (WorkerThreadState)state!;
        workerState.Scheduler.RunWorkerLoop(workerState.WorkerIndex);
    }

    private void EmitPublished(ValueJobHandle handle, ref JobDescriptor descriptor)
    {
        JobProfilerCallback? callback = _profilerHooks?.Published;
        if (callback is null)
        {
            return;
        }

        JobProfilerEvent profilerEvent = CreateProfilerEvent(handle, ref descriptor);
        callback(in profilerEvent);
    }

    private void EmitStarted(ValueJobHandle handle, ref JobDescriptor descriptor)
    {
        JobProfilerCallback? callback = _profilerHooks?.Started;
        if (callback is null)
        {
            return;
        }

        JobProfilerEvent profilerEvent = CreateProfilerEvent(handle, ref descriptor);
        callback(in profilerEvent);
    }

    private void EmitCompleted(ValueJobHandle handle, ref JobDescriptor descriptor)
    {
        JobProfilerCallback? callback = _profilerHooks?.Completed;
        if (callback is null)
        {
            return;
        }

        JobProfilerEvent profilerEvent = CreateProfilerEvent(handle, ref descriptor);
        callback(in profilerEvent);
    }

    private void EmitFailed(ValueJobHandle handle, ref JobDescriptor descriptor)
    {
        JobProfilerCallback? callback = _profilerHooks?.Failed;
        if (callback is null)
        {
            return;
        }

        JobProfilerEvent profilerEvent = CreateProfilerEvent(handle, ref descriptor);
        callback(in profilerEvent);
    }

    private static JobProfilerEvent CreateProfilerEvent(ValueJobHandle handle, ref JobDescriptor descriptor)
    {
        return new JobProfilerEvent(
            handle,
            descriptor.Kind,
            descriptor.DependencyCount,
            CreateBatchInfo(ref descriptor),
            Stopwatch.GetTimestamp());
    }

    private static JobBatchInfo CreateBatchInfo(ref JobDescriptor descriptor)
    {
        return descriptor.Kind == JobDescriptorKind.ParallelBatch
            ? new JobBatchInfo(
                descriptor.StartIndex,
                descriptor.EndIndex,
                descriptor.BatchSize,
                descriptor.BatchCount)
            : default;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool CanStorePayload<TJob>()
        where TJob : unmanaged
    {
        return Unsafe.SizeOf<TJob>() <= MaxPayloadByteLength;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void* PayloadPointer(int descriptorIndex)
    {
        return _payloadBasePointer + descriptorIndex * _payloadStride;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void EnsureNotDisposed()
    {
        if (_isDisposed)
        {
            throw new ObjectDisposedException(nameof(JobScheduler));
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool IsValidWorkerIndex(int workerIndex)
    {
        return (uint)workerIndex < (uint)WorkerCount;
    }

    private static int AlignUp(int value, int alignment)
    {
        return checked((value + alignment - 1) & -alignment);
    }

    private enum WorkerRunResult
    {
        Empty,
        PendingDependency,
        Completed
    }

    private sealed class WorkerThreadState
    {
        public WorkerThreadState(JobScheduler scheduler, int workerIndex)
        {
            Scheduler = scheduler;
            WorkerIndex = workerIndex;
        }

        public JobScheduler Scheduler { get; }
        public int WorkerIndex { get; }
    }
}
