using System.Diagnostics;
using Karpik.Memory;

const int Iterations = 10_000;

Console.WriteLine("Karpik.Memory lightweight benchmark");
Console.WriteLine($"Iterations: {Iterations}");

Measure(
    "NativeArray<int> traversal",
    Iterations,
    static () =>
    {
        NativeArray<int> array = new(1024);
        for (int i = 0; i < array.Length; i++)
        {
            array[i] = i;
        }
        return array;
    },
    static array =>
    {
        for (int i = 0; i < array.Length; i++)
        {
            array[i]++;
        }
    });

Measure(
    "NativeLinearAllocator allocate/reset",
    Iterations,
    static () => new NativeLinearAllocator(byteCapacity: 4096, alignment: 16),
    static allocator =>
    {
        allocator.Reset();
        for (int i = 0; i < 32; i++)
        {
            NativeMemorySlice slice = allocator.Allocate(64, 16);
            slice.Bytes[0] = (byte)i;
        }
    });

Measure(
    "NativePool<int> rent/return",
    Iterations,
    static () => new NativePool<int>(capacity: 64),
    static pool =>
    {
        for (int i = 0; i < 64; i++)
        {
            NativePoolHandle<int> handle = pool.Rent();
            handle.Value = i;
            pool.Return(handle);
        }
    });

Measure(
    "NativeResult<int> handle write",
    Iterations,
    static () => new NativeResult<int>(),
    static result =>
    {
        NativeResultHandle<int> handle = result.AsHandle();
        for (int i = 0; i < 1024; i++)
        {
            handle.Value = i;
        }
    });

Measure(
    "NativeArena outside-frame allocate/reset",
    Iterations,
    static () => new NativeArena(blockByteLength: 4096, alignment: 16),
    static arena =>
    {
        arena.Reset();
        for (int i = 0; i < 32; i++)
        {
            NativeMemorySlice slice = arena.Allocate(64, 16);
            slice.Bytes[0] = (byte)i;
        }
    });

static void Measure<TState>(string name, int iterations, Func<TState> createState, Action<TState> action)
    where TState : IDisposable
{
    using TState state = createState();

    for (int i = 0; i < 128; i++)
    {
        action(state);
    }

    GC.Collect();
    GC.WaitForPendingFinalizers();
    GC.Collect();

    long beforeBytes = GC.GetAllocatedBytesForCurrentThread();
    long beforeTimestamp = Stopwatch.GetTimestamp();

    for (int i = 0; i < iterations; i++)
    {
        action(state);
    }

    long elapsedTicks = Stopwatch.GetTimestamp() - beforeTimestamp;
    long allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - beforeBytes;
    double elapsedMs = elapsedTicks * 1000.0 / Stopwatch.Frequency;

    Console.WriteLine($"{name}: {elapsedMs:F3} ms, {allocatedBytes} B managed");
}
