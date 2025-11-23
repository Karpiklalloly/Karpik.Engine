using System.Runtime.InteropServices;

namespace Karpik.Jobs;

public unsafe struct SimpleNativeArray<T> : IDisposable where T : unmanaged
{
    private T* _ptr;
    public readonly int Length;

    public SimpleNativeArray(int length)
    {
        Length = length;
        _ptr = (T*)NativeMemory.Alloc((nuint)(length * sizeof(T)));
    }

    public ref T this[int index] => ref _ptr[index];

    public void Dispose()
    {
        if (_ptr != null)
        {
            NativeMemory.Free(_ptr);
            _ptr = null;
        }
    }
}
