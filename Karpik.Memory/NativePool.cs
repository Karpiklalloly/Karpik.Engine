using System.Runtime.CompilerServices;

namespace Karpik.Memory;

public sealed unsafe class NativePool<T> : IDisposable where T : unmanaged
{
    private readonly NativeAllocation _allocation;
    private readonly T* _values;
    private readonly NativePoolSlot* _slots;
    private int _freeHead;
    private bool _isDisposed;

    public NativePool(int capacity)
    {
        if (capacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity), capacity, "Capacity must be greater than zero.");
        }

        Capacity = capacity;

        nuint valueAlignment = NativeMemoryMath.PowerOfTwoAtLeast((nuint)Unsafe.SizeOf<T>());
        nuint slotAlignment = NativeMemoryMath.PowerOfTwoAtLeast((nuint)Unsafe.SizeOf<NativePoolSlot>());
        nuint alignment = valueAlignment > slotAlignment ? valueAlignment : slotAlignment;
        nuint valueBytes = checked((nuint)capacity * (nuint)Unsafe.SizeOf<T>());
        nuint slotOffset = NativeMemoryMath.AlignUp(valueBytes, slotAlignment);
        nuint slotBytes = checked((nuint)capacity * (nuint)Unsafe.SizeOf<NativePoolSlot>());
        nuint byteLength = NativeMemoryMath.AlignUp(checked(slotOffset + slotBytes), alignment);

        _allocation = NativeAllocation.Allocate(byteLength, alignment, NativeAllocationKind.Pool);
        byte* basePointer = (byte*)_allocation.Pointer;
        _values = (T*)basePointer;
        _slots = (NativePoolSlot*)(basePointer + slotOffset);
        _freeHead = 0;

        for (int i = 0; i < capacity; i++)
        {
            _slots[i] = new NativePoolSlot
            {
                NextFree = i + 1,
                Generation = 1,
                Rented = 0
            };
        }

        _slots[capacity - 1].NextFree = -1;
    }

    public int Capacity { get; }
    public int RentedCount { get; private set; }

    public NativePoolHandle<T> Rent()
    {
        EnsureNotDisposed();

        int index = _freeHead;
        if (index < 0)
        {
            throw new InvalidOperationException($"Native pool capacity {Capacity} is exhausted.");
        }

        NativePoolSlot* slot = _slots + index;
        _freeHead = slot->NextFree;
        slot->NextFree = -1;
        slot->Rented = 1;
        RentedCount++;

        return new NativePoolHandle<T>(
            _values + index,
            slot,
            index,
            slot->Generation,
            _allocation.Token);
    }

    public void Return(NativePoolHandle<T> handle)
    {
        EnsureNotDisposed();
        handle.ValidateForOwner(_allocation.Token);

        NativePoolSlot* slot = handle.Slot;
        slot->Rented = 0;
        unchecked
        {
            slot->Generation++;
        }

        slot->NextFree = _freeHead;
        _freeHead = handle.Index;
        RentedCount--;
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            throw new ObjectDisposedException(nameof(NativePool<T>));
        }

        _isDisposed = true;
        _allocation.Dispose();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void EnsureNotDisposed()
    {
        if (_isDisposed)
        {
            throw new ObjectDisposedException(nameof(NativePool<T>));
        }
    }
}
