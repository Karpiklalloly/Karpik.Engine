using Karpik.Memory;

namespace Karpik.Jobs;

public struct SimpleNativeArray<T> : IDisposable where T : unmanaged
{
    private NativeArray<T>? _array;
    public readonly int Length;

    public SimpleNativeArray(int length)
    {
        Length = length;
        _array = new NativeArray<T>(length);
    }

    public ref T this[int index]
    {
        get
        {
            NativeArray<T> array = _array ?? throw new ObjectDisposedException(nameof(SimpleNativeArray<T>));
            return ref array[index];
        }
    }

    public void Dispose()
    {
        NativeArray<T> array = _array ?? throw new ObjectDisposedException(nameof(SimpleNativeArray<T>));
        _array = null;
        array.Dispose();
    }
}
