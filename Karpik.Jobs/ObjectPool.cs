using System.Collections.Concurrent;
using System.Runtime.CompilerServices;

namespace Karpik.Jobs;

internal sealed class ObjectPool<T> : IDisposable where T : class, new()
{
    private readonly ConcurrentBag<T> _items = new();
    private readonly Func<T> _factory;

    public ObjectPool(Func<T> factory, int initialCapacity)
    {
        _factory = factory;
        for (int i = 0; i < initialCapacity; i++) _items.Add(_factory());
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public T? Rent()
    {
        if (_items.TryTake(out var item)) return item;
        return _factory();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Return(T item)
    {
        _items.Add(item);
    }

    public void Dispose()
    {
        _items.Clear();
    }
}