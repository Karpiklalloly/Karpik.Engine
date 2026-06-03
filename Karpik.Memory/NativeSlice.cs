using System.Runtime.CompilerServices;

namespace Karpik.Memory;

public readonly unsafe struct NativeSlice<T> where T : unmanaged
{
    private readonly T* _pointer;
    private readonly NativeAllocationToken _token;

    internal NativeSlice(T* pointer, int length, NativeAllocationToken token)
    {
        _pointer = pointer;
        Length = length;
        _token = token;
    }

    public int Length { get; }

    public Span<T> Span
    {
        get
        {
            Validate();
            return Length == 0 ? Span<T>.Empty : new Span<T>(_pointer, Length);
        }
    }

    public ref T this[int index]
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            Validate();
            NativeArray<T>.CheckIndex(index, Length);
            return ref _pointer[index];
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void Validate()
    {
#if DEBUG
        if (Length != 0)
        {
            NativeMemoryDiagnostics.Validate(_token);
        }
#endif
    }
}
