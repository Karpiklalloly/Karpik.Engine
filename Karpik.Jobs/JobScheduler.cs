using System.Runtime.CompilerServices;
using Karpik.Memory;

namespace Karpik.Jobs;

public sealed unsafe class JobScheduler : IDisposable
{
    public const int DefaultPayloadAlignment = 16;

    private readonly JobDescriptorPool _descriptors;
    private readonly NativeLinearAllocator _payloadAllocator;
    private readonly NativeMemorySlice _payloadBlock;
    private readonly NativeArray<ValueJobHandle> _dependencies;
    private readonly NativeArray<int> _completedGenerations;
    private readonly NativeArray<int> _failedGenerations;
    private readonly byte* _payloadBasePointer;
    private readonly int _payloadStride;
    private ValueJobHandle _lastExceptionHandle;
    private Exception? _lastException;
    private bool _isDisposed;

    public JobScheduler(
        int capacity,
        int maxPayloadByteLength,
        int payloadAlignment = DefaultPayloadAlignment,
        int maxDependenciesPerJob = 4)
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

        Capacity = capacity;
        MaxPayloadByteLength = maxPayloadByteLength;
        PayloadAlignment = payloadAlignment;
        MaxDependenciesPerJob = maxDependenciesPerJob;

        _payloadStride = AlignUp(maxPayloadByteLength, payloadAlignment);
        int payloadByteCapacity = checked(capacity * _payloadStride);

        _descriptors = new JobDescriptorPool(capacity);
        _payloadAllocator = new NativeLinearAllocator(payloadByteCapacity, payloadAlignment);
        _payloadBlock = _payloadAllocator.Allocate((nuint)payloadByteCapacity, (nuint)payloadAlignment);
        _dependencies = new NativeArray<ValueJobHandle>(checked(capacity * maxDependenciesPerJob));
        _completedGenerations = new NativeArray<int>(capacity);
        _failedGenerations = new NativeArray<int>(capacity);
        _payloadBasePointer = (byte*)_payloadBlock.Pointer;
    }

    public int Capacity { get; }
    public int MaxPayloadByteLength { get; }
    public int PayloadAlignment { get; }
    public int MaxDependenciesPerJob { get; }
    public int ScheduledCount => _descriptors.RentedCount;
    public int AvailableDescriptorCount => _descriptors.AvailableCount;

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

        if (!CanStorePayload<TJob>() || !CanStoreDependencies(dependencies))
        {
            handle = default;
            return false;
        }

        if (!_descriptors.TryRent(JobDescriptorKind.Single, out JobDescriptorHandle descriptorHandle))
        {
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

        if (length < 0 || batchSize <= 0 || !CanStorePayload<TJob>() || !CanStoreDependencies(dependencies))
        {
            handle = default;
            return false;
        }

        if (!_descriptors.TryRent(JobDescriptorKind.ParallelBatch, out JobDescriptorHandle descriptorHandle))
        {
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
        descriptor.BatchCount = length == 0 ? 0 : (length + batchSize - 1) / batchSize;
        descriptor.Execute = null;
        descriptor.ExecuteFor = &JobForExecutor<TJob>.Execute;
        StoreDependencies(ref descriptor, descriptorHandle.Index, dependencies);

        handle = new ValueJobHandle(descriptorHandle);
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
                descriptor.Execute(descriptor.PayloadPointer);
                MarkCompleted(handle);
                return true;
            }

            if (descriptor.Kind == JobDescriptorKind.ParallelBatch)
            {
                for (int i = descriptor.StartIndex; i < descriptor.EndIndex; i++)
                {
                    descriptor.ExecuteFor(descriptor.PayloadPointer, i);
                }

                MarkCompleted(handle);
                return true;
            }

            throw new InvalidOperationException($"Unsupported job descriptor kind {descriptor.Kind}.");
        }
        catch (Exception exception)
        {
            MarkFailed(handle, exception);
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
               _completedGenerations[handle.Descriptor.Index] == handle.Descriptor.Generation;
    }

    public bool HasException(ValueJobHandle handle)
    {
        EnsureNotDisposed();
        return handle.IsValid &&
               (uint)handle.Descriptor.Index < (uint)Capacity &&
               _failedGenerations[handle.Descriptor.Index] == handle.Descriptor.Generation;
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

    public void Dispose()
    {
        if (_isDisposed)
        {
            throw new ObjectDisposedException(nameof(JobScheduler));
        }

        _isDisposed = true;
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
            return false;
        }

        for (int i = 0; i < dependencies.Length; i++)
        {
            ValueJobHandle dependency = dependencies[i];
            if (!dependency.IsValid ||
                (uint)dependency.Descriptor.Index >= (uint)Capacity ||
                (!IsCompleted(dependency) && !_descriptors.IsRented(dependency.Descriptor)))
            {
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
        _completedGenerations[handle.Descriptor.Index] = handle.Descriptor.Generation;
    }

    private void MarkFailed(ValueJobHandle handle, Exception exception)
    {
        MarkCompleted(handle);
        _failedGenerations[handle.Descriptor.Index] = handle.Descriptor.Generation;
        _lastExceptionHandle = handle;
        _lastException = exception;
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

    private static int AlignUp(int value, int alignment)
    {
        return checked((value + alignment - 1) & -alignment);
    }
}
