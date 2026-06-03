using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Karpik.Memory;

internal sealed unsafe class NativeAllocation : IDisposable
{
    private void* _pointer;
    private int _version;

    private NativeAllocation(
        void* pointer,
        long id,
        nuint byteLength,
        nuint alignment,
        NativeAllocationKind kind)
    {
        _pointer = pointer;
        Id = id;
        ByteLength = byteLength;
        Alignment = alignment;
        Kind = kind;
        _version = 1;
    }

    public void* Pointer => _pointer;
    public long Id { get; }
    public nuint ByteLength { get; }
    public nuint Alignment { get; }
    public NativeAllocationKind Kind { get; }
    public bool IsDisposed => _pointer == null;
    public NativeAllocationToken Token => new(Id, _version);

    public static NativeAllocation Allocate(
        nuint byteLength,
        nuint alignment,
        NativeAllocationKind kind)
    {
        ValidateByteLength(byteLength);
        ValidateAlignment(alignment);

        void* pointer = NativeMemory.AlignedAlloc(byteLength, alignment);
        if (pointer == null)
        {
            throw new OutOfMemoryException(
                $"Failed to allocate {byteLength} native bytes with alignment {alignment}.");
        }

        long id = NativeMemoryDiagnostics.NextAllocationId();
        NativeMemoryDiagnostics.Register(id, byteLength, alignment, kind);
        return new NativeAllocation(pointer, id, byteLength, alignment, kind);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Validate(NativeAllocationToken token)
    {
        if (_pointer == null)
        {
            throw new ObjectDisposedException(nameof(NativeAllocation));
        }

        if (token.Id != Id || token.Version != _version)
        {
            throw new InvalidOperationException(
                $"Native allocation token is stale or belongs to another allocation. Expected id {Id}, version {_version}.");
        }
    }

    public void Dispose()
    {
        if (_pointer == null)
        {
            throw new ObjectDisposedException(nameof(NativeAllocation));
        }

        void* pointer = _pointer;
        _pointer = null;
        unchecked
        {
            _version++;
        }

        NativeMemoryDiagnostics.Unregister(Id);
        NativeMemory.AlignedFree(pointer);
    }

    private static void ValidateByteLength(nuint byteLength)
    {
        if (byteLength == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(byteLength), byteLength, "Byte length must be greater than zero.");
        }
    }

    private static void ValidateAlignment(nuint alignment)
    {
        if (alignment == 0 || (alignment & (alignment - 1)) != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(alignment), alignment, "Alignment must be a non-zero power of two.");
        }
    }
}
