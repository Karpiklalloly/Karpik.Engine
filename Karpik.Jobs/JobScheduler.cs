using System.Runtime.CompilerServices;
using Karpik.Memory;

namespace Karpik.Jobs;

public sealed unsafe class JobScheduler : IDisposable
{
    public const int DefaultPayloadAlignment = 16;

    private readonly JobDescriptorPool _descriptors;
    private readonly NativeLinearAllocator _payloadAllocator;
    private readonly NativeMemorySlice _payloadBlock;
    private readonly byte* _payloadBasePointer;
    private readonly int _payloadStride;
    private bool _isDisposed;

    public JobScheduler(
        int capacity,
        int maxPayloadByteLength,
        int payloadAlignment = DefaultPayloadAlignment)
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

        Capacity = capacity;
        MaxPayloadByteLength = maxPayloadByteLength;
        PayloadAlignment = payloadAlignment;

        _payloadStride = AlignUp(maxPayloadByteLength, payloadAlignment);
        int payloadByteCapacity = checked(capacity * _payloadStride);

        _descriptors = new JobDescriptorPool(capacity);
        _payloadAllocator = new NativeLinearAllocator(payloadByteCapacity, payloadAlignment);
        _payloadBlock = _payloadAllocator.Allocate((nuint)payloadByteCapacity, (nuint)payloadAlignment);
        _payloadBasePointer = (byte*)_payloadBlock.Pointer;
    }

    public int Capacity { get; }
    public int MaxPayloadByteLength { get; }
    public int PayloadAlignment { get; }
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
        EnsureNotDisposed();

        if (!CanStorePayload<TJob>())
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
        EnsureNotDisposed();

        if (length < 0 || batchSize <= 0 || !CanStorePayload<TJob>())
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

        handle = new ValueJobHandle(descriptorHandle);
        return true;
    }

    public void Complete(ValueJobHandle handle)
    {
        EnsureNotDisposed();

        ref JobDescriptor descriptor = ref _descriptors.Get(handle.Descriptor);
        try
        {
            if (descriptor.Kind == JobDescriptorKind.Single)
            {
                descriptor.Execute(descriptor.PayloadPointer);
                return;
            }

            if (descriptor.Kind == JobDescriptorKind.ParallelBatch)
            {
                for (int i = descriptor.StartIndex; i < descriptor.EndIndex; i++)
                {
                    descriptor.ExecuteFor(descriptor.PayloadPointer, i);
                }

                return;
            }

            throw new InvalidOperationException($"Unsupported job descriptor kind {descriptor.Kind}.");
        }
        finally
        {
            _descriptors.TryReturn(handle.Descriptor);
        }
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            throw new ObjectDisposedException(nameof(JobScheduler));
        }

        _isDisposed = true;
        _payloadAllocator.Dispose();
        _descriptors.Dispose();
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
