using System.Runtime.CompilerServices;

namespace Karpik.Memory;

public readonly unsafe struct NativeResultHandle<T> where T : unmanaged
{
    private readonly T* _pointer;
    private readonly NativeAllocationToken _token;

    internal NativeResultHandle(T* pointer, NativeAllocationToken token)
    {
        _pointer = pointer;
        _token = token;
    }

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
    private void Validate()
    {
#if DEBUG
        NativeMemoryDiagnostics.Validate(_token);
#endif
    }
}
