using System.Runtime.CompilerServices;

namespace Karpik.Memory;

public sealed unsafe class NativeResult<T> : IDisposable where T : unmanaged
{
    private readonly NativeAllocation _allocation;
    private readonly T* _pointer;
    private bool _isDisposed;

    public NativeResult()
    {
        nuint requestedBytes = (nuint)Unsafe.SizeOf<T>();
        nuint alignment = NativeMemoryMath.PowerOfTwoAtLeast(requestedBytes);
        nuint byteLength = NativeMemoryMath.AlignUp(requestedBytes, alignment);
        _allocation = NativeAllocation.Allocate(byteLength, alignment, NativeAllocationKind.Result);
        _pointer = (T*)_allocation.Pointer;
    }

    public ref T Value
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            EnsureNotDisposed();
            return ref *_pointer;
        }
    }

    public NativeResultHandle<T> AsHandle()
    {
        EnsureNotDisposed();
        return new NativeResultHandle<T>(_pointer, _allocation.Token);
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            throw new ObjectDisposedException(nameof(NativeResult<T>));
        }

        _isDisposed = true;
        _allocation.Dispose();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void EnsureNotDisposed()
    {
        if (_isDisposed)
        {
            throw new ObjectDisposedException(nameof(NativeResult<T>));
        }
    }
}
