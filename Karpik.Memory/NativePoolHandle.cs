using System.Runtime.CompilerServices;

namespace Karpik.Memory;

public readonly unsafe struct NativePoolHandle<T> where T : unmanaged
{
    private readonly T* _pointer;
    private readonly int _generation;
    private readonly NativeAllocationToken _token;

    internal NativePoolHandle(
        T* pointer,
        NativePoolSlot* slot,
        int index,
        int generation,
        NativeAllocationToken token)
    {
        _pointer = pointer;
        Slot = slot;
        Index = index;
        _generation = generation;
        _token = token;
    }

    internal NativePoolSlot* Slot { get; }
    internal int Index { get; }

    public ref T Value
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            Validate();
            return ref *_pointer;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal void ValidateForOwner(NativeAllocationToken ownerToken)
    {
        if (_token.Id != ownerToken.Id || _token.Version != ownerToken.Version)
        {
            throw new InvalidOperationException("Native pool handle belongs to another pool.");
        }

        Validate();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void Validate()
    {
#if DEBUG
        NativeMemoryDiagnostics.Validate(_token);
#endif
        if (Slot->Rented == 0 || Slot->Generation != _generation)
        {
            throw new InvalidOperationException("Native pool handle is stale or has already been returned.");
        }
    }
}
