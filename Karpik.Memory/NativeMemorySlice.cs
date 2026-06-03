using System.Runtime.CompilerServices;

namespace Karpik.Memory;

public readonly unsafe struct NativeMemorySlice
{
    private readonly byte* _pointer;
    private readonly NativeAllocationToken _token;

    internal NativeMemorySlice(byte* pointer, nuint byteLength, NativeAllocationToken token)
    {
        _pointer = pointer;
        ByteLength = byteLength;
        _token = token;
    }

    public void* Pointer => _pointer;
    public nuint ByteLength { get; }

    public Span<byte> Bytes
    {
        get
        {
            Validate();
            return new Span<byte>(_pointer, checked((int)ByteLength));
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal void Validate()
    {
#if DEBUG
        NativeMemoryDiagnostics.Validate(_token);
        if (_token.BorrowVersion != 0)
        {
            NativeLinearAllocator.ValidateBorrowVersion(_token);
        }
#endif
    }
}
