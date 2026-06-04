using System.Runtime.CompilerServices;
using Karpik.Memory;

namespace Karpik.Jobs;

internal sealed class WorkStealingDeque<T> : IDisposable where T : unmanaged
{
    private readonly NativeArray<T> _items;
    private readonly int _mask;
    private int _top;
    private int _bottom;
    private bool _isDisposed;

    public WorkStealingDeque(int capacity)
    {
        if (capacity <= 0 || (capacity & (capacity - 1)) != 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(capacity),
                capacity,
                "Capacity must be a positive power of two.");
        }

        Capacity = capacity;
        _mask = capacity - 1;
        _items = new NativeArray<T>(capacity);
    }

    public int Capacity { get; }

    public int Count
    {
        get
        {
            EnsureNotDisposed();
            int bottom = Volatile.Read(ref _bottom);
            int top = Volatile.Read(ref _top);
            int count = bottom - top;
            if (count <= 0)
            {
                return 0;
            }

            return count > Capacity ? Capacity : count;
        }
    }

    public bool TryPushBottom(T item)
    {
        EnsureNotDisposed();

        int bottom = Volatile.Read(ref _bottom);
        int top = Volatile.Read(ref _top);
        if (bottom - top >= Capacity)
        {
            return false;
        }

        _items[bottom & _mask] = item;

        // Publish the written slot before advancing bottom so thieves never see an uninitialized item.
        Volatile.Write(ref _bottom, bottom + 1);
        return true;
    }

    public bool TryPopBottom(out T item)
    {
        EnsureNotDisposed();

        int bottom = Volatile.Read(ref _bottom) - 1;
        Volatile.Write(ref _bottom, bottom);
        int top = Volatile.Read(ref _top);
        int count = bottom - top;

        if (count < 0)
        {
            Volatile.Write(ref _bottom, top);
            item = default;
            return false;
        }

        item = _items[bottom & _mask];
        if (count > 0)
        {
            return true;
        }

        // Last item race: owner and thief contend through top. Only the winner keeps the item.
        if (Interlocked.CompareExchange(ref _top, top + 1, top) == top)
        {
            Volatile.Write(ref _bottom, top + 1);
            return true;
        }

        item = default;
        Volatile.Write(ref _bottom, top + 1);
        return false;
    }

    public bool TryStealTop(out T item)
    {
        EnsureNotDisposed();

        int top = Volatile.Read(ref _top);
        int bottom = Volatile.Read(ref _bottom);
        if (bottom - top <= 0)
        {
            item = default;
            return false;
        }

        item = _items[top & _mask];

        // Claim the top slot after reading it. Losing the CAS means another thief/owner won the item.
        if (Interlocked.CompareExchange(ref _top, top + 1, top) == top)
        {
            return true;
        }

        item = default;
        return false;
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            throw new ObjectDisposedException(nameof(WorkStealingDeque<T>));
        }

        _isDisposed = true;
        _items.Dispose();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void EnsureNotDisposed()
    {
        if (_isDisposed)
        {
            throw new ObjectDisposedException(nameof(WorkStealingDeque<T>));
        }
    }
}
