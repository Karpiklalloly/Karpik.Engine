using System.Runtime.CompilerServices;

namespace Karpik.Memory;

public sealed unsafe class NativeArray<T> : IDisposable where T : unmanaged
{
    private readonly NativeAllocation? _allocation;
    private readonly T* _pointer;
    private bool _isDisposed;

    public NativeArray(int length)
    {
        if (length < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(length), length, "Length must be greater than or equal to zero.");
        }

        Length = length;
        if (length == 0)
        {
            _allocation = null;
            _pointer = null;
            return;
        }

        nuint requestedBytes = checked((nuint)length * (nuint)Unsafe.SizeOf<T>());
        nuint alignment = NativeMemoryMath.PowerOfTwoAtLeast((nuint)Unsafe.SizeOf<T>());
        nuint byteLength = NativeMemoryMath.AlignUp(requestedBytes, alignment);
        _allocation = NativeAllocation.Allocate(byteLength, alignment, NativeAllocationKind.Array);
        _pointer = (T*)_allocation.Pointer;
    }

    public int Length { get; }

    public Span<T> Span
    {
        get
        {
            EnsureNotDisposed();
            return Length == 0 ? Span<T>.Empty : new Span<T>(_pointer, Length);
        }
    }

    public ReadOnlySpan<T> ReadOnlySpan
    {
        get
        {
            EnsureNotDisposed();
            return Length == 0 ? ReadOnlySpan<T>.Empty : new ReadOnlySpan<T>(_pointer, Length);
        }
    }

    public ref T this[int index]
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            EnsureNotDisposed();
            CheckIndex(index, Length);
            return ref _pointer[index];
        }
    }

    public NativeSlice<T> AsSlice()
    {
        EnsureNotDisposed();
        return Length == 0
            ? default
            : new NativeSlice<T>(_pointer, Length, _allocation!.Token);
    }

    public void Clear()
    {
        Span.Clear();
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            throw new ObjectDisposedException(nameof(NativeArray<T>));
        }

        _isDisposed = true;
        _allocation?.Dispose();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void EnsureNotDisposed()
    {
        if (_isDisposed)
        {
            throw new ObjectDisposedException(nameof(NativeArray<T>));
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static void CheckIndex(int index, int length)
    {
        if ((uint)index >= (uint)length)
        {
            throw new IndexOutOfRangeException($"Index {index} is outside native array length {length}.");
        }
    }
}
