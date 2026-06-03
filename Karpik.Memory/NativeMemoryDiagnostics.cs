using System.Collections.Concurrent;
using System.Threading;

namespace Karpik.Memory;

internal static class NativeMemoryDiagnostics
{
    private static readonly ConcurrentDictionary<long, NativeAllocationInfo> ActiveAllocations = new();
    private static long _nextAllocationId;

    public static int ActiveAllocationCount => ActiveAllocations.Count;

    public static long NextAllocationId()
    {
        return Interlocked.Increment(ref _nextAllocationId);
    }

    public static void Register(
        long id,
        nuint byteLength,
        nuint alignment,
        NativeAllocationKind kind)
    {
        if (!ActiveAllocations.TryAdd(id, new NativeAllocationInfo(byteLength, alignment, kind, version: 1)))
        {
            throw new InvalidOperationException($"Native allocation id '{id}' is already registered.");
        }
    }

    public static void Unregister(long id)
    {
        if (!ActiveAllocations.TryRemove(id, out _))
        {
            throw new InvalidOperationException($"Native allocation id '{id}' is not registered.");
        }
    }

    public static void Validate(NativeAllocationToken token)
    {
        if (!ActiveAllocations.TryGetValue(token.Id, out NativeAllocationInfo info))
        {
            throw new ObjectDisposedException("Native allocation");
        }

        if (info.Version != token.Version)
        {
            throw new InvalidOperationException(
                $"Native allocation token is stale. Expected version {info.Version}, got {token.Version}.");
        }
    }

    private readonly struct NativeAllocationInfo
    {
        public readonly nuint ByteLength;
        public readonly nuint Alignment;
        public readonly NativeAllocationKind Kind;
        public readonly int Version;

        public NativeAllocationInfo(nuint byteLength, nuint alignment, NativeAllocationKind kind, int version)
        {
            ByteLength = byteLength;
            Alignment = alignment;
            Kind = kind;
            Version = version;
        }
    }
}
