using System.Runtime.CompilerServices;
using Karpik.Memory;

namespace Karpik.Jobs;

internal sealed class JobDescriptorPool : IDisposable
{
    private readonly NativeArray<JobDescriptor> _descriptors;
    private readonly NativeArray<int> _freeStack;
    private int _availableCount;
    private bool _isDisposed;

    public JobDescriptorPool(int capacity)
    {
        if (capacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity), capacity, "Capacity must be greater than zero.");
        }

        Capacity = capacity;
        _descriptors = new NativeArray<JobDescriptor>(capacity);
        _freeStack = new NativeArray<int>(capacity);
        _availableCount = capacity;

        for (int i = 0; i < capacity; i++)
        {
            ref JobDescriptor descriptor = ref _descriptors[i];
            descriptor.Kind = JobDescriptorKind.Empty;
            descriptor.Generation = 1;
            descriptor.ExceptionIndex = -1;
            _freeStack[i] = i;
        }
    }

    public int Capacity { get; }
    public int RentedCount { get; private set; }
    public int AvailableCount => _availableCount;

    public bool TryRent(JobDescriptorKind kind, out JobDescriptorHandle handle)
    {
        EnsureNotDisposed();

        if (_availableCount == 0)
        {
            handle = default;
            return false;
        }

        int index = _freeStack[--_availableCount];
        ref JobDescriptor descriptor = ref _descriptors[index];
        descriptor.ResetForRent(kind);
        RentedCount++;

        handle = new JobDescriptorHandle(index, descriptor.Generation);
        return true;
    }

    public bool TryReturn(JobDescriptorHandle handle)
    {
        EnsureNotDisposed();

        if (!TryValidate(handle))
        {
            return false;
        }

        ref JobDescriptor descriptor = ref _descriptors[handle.Index];
        descriptor.ResetAfterReturn();
        unchecked
        {
            descriptor.Generation++;
            if (descriptor.Generation == 0)
            {
                descriptor.Generation = 1;
            }
        }

        _freeStack[_availableCount++] = handle.Index;
        RentedCount--;
        return true;
    }

    public ref JobDescriptor Get(JobDescriptorHandle handle)
    {
        EnsureNotDisposed();

        if (!TryValidate(handle))
        {
            throw new InvalidOperationException("Job descriptor handle is stale or does not belong to a rented descriptor.");
        }

        return ref _descriptors[handle.Index];
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            throw new ObjectDisposedException(nameof(JobDescriptorPool));
        }

        _isDisposed = true;
        _freeStack.Dispose();
        _descriptors.Dispose();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool TryValidate(JobDescriptorHandle handle)
    {
        if (!handle.IsValid || (uint)handle.Index >= (uint)Capacity)
        {
            return false;
        }

        ref JobDescriptor descriptor = ref _descriptors[handle.Index];
        return descriptor.Kind != JobDescriptorKind.Empty &&
               descriptor.Generation == handle.Generation;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void EnsureNotDisposed()
    {
        if (_isDisposed)
        {
            throw new ObjectDisposedException(nameof(JobDescriptorPool));
        }
    }
}
