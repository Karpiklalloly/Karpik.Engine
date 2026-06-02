using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace Karpik.Engine.Shared.UnsafeUtilities;

/// <summary>
/// High-performance list with zero boxing for value types.
/// Uses struct enumerator to avoid GC allocations.
/// </summary>
public struct ResizableArray<T> : IEnumerable<T> where T : struct
{
    private const int DefaultCapacity = 4;
    
    private T[] _items;
    private int _size;

    public int Capacity
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _items.Length;
        set
        {
            if (value < _size)
            {
                ThrowHelper.ArgumentOutOfRange_IndexMustBeLessThanSize();
            }

            if (value == _items.Length) return;
            
            if (value > 0)
            {
                var newItems = new T[value];
                if (_size > 0)
                {
                    Array.Copy(_items, 0, newItems, 0, _size);
                }
                _items = newItems;
            }
            else
            {
                _items = Array.Empty<T>();
            }
        }
    }

    public int Count
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _size;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ResizableArray(int capacity)
    {
        if (capacity < 0)
        {
            ThrowHelper.ArgumentOutOfRange_NeedNonNegNum();
        }

        _items = capacity == 0 ? Array.Empty<T>() : new T[capacity];
        _size = 0;
    }

    public ResizableArray()
    {
        _items = Array.Empty<T>();
        _size = 0;
    }

    /// <summary>
    /// Direct indexer with bounds checking.
    /// </summary>
    public ref T this[int index]
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            if ((uint)index >= (uint)_size)
            {
                ThrowHelper.ArgumentOutOfRange_IndexMustBeLessThanCount();
            }
            return ref _items[index];
        }
    }

    /// <summary>
    /// Returns a Span over the current items (no copy, no allocation).
    /// </summary>
    public Span<T> AsSpan()
    {
        return new Span<T>(_items, 0, _size);
    }

    /// <summary>
    /// Returns a ReadOnlySpan over the current items (no copy, no allocation).
    /// </summary>
    public ReadOnlySpan<T> AsReadOnlySpan()
    {
        return new ReadOnlySpan<T>(_items, 0, _size);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Add(T item)
    {
        T[] array = _items;
        int size = _size;
        if ((uint)size < (uint)array.Length)
        {
            _size = size + 1;
            array[size] = item;
        }
        else
        {
            AddWithResize(item);
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void AddWithResize(T item)
    {
        Debug.Assert(_size == _items.Length);
        Grow(_size + 1);
        _items[_size] = item;
        _size++;
    }

    /// <summary>
    /// Adds multiple items at once. Returns the index of the first added item.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int AddRange(ReadOnlySpan<T> items)
    {
        if (items.IsEmpty) return _size;
        
        int count = items.Length;
        int startIndex = _size;
        int newSize = _size + count;
        
        if (newSize > _items.Length)
        {
            Grow(newSize);
        }
        
        items.CopyTo(new Span<T>(_items, startIndex, count));
        _size = newSize;
        
        return startIndex;
    }

    /// <summary>
    /// Adds an item without bounds checking (caller must ensure capacity).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void AddUnsafe(T item)
    {
        _items[_size] = item;
        _size++;
    }

    /// <summary>
    /// Ensures the list has at least the specified capacity.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void EnsureCapacity(int capacity)
    {
        if ((uint)capacity > (uint)_items.Length)
        {
            Grow(capacity);
        }
    }

    /// <summary>
    /// Clears all items. Sets size to 0.
    /// Does not null out references (use TrimExcess for that).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Clear()
    {
        _size = 0;
    }

    /// <summary>
    /// Clears and nulls out all references (for GC).
    /// </summary>
    public void ClearWithNulls()
    {
        if (_size > 0)
        {
            Array.Clear(_items, 0, _size);
            _size = 0;
        }
    }

    /// <summary>
    /// Removes the item at the specified index.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void RemoveAt(int index)
    {
        if ((uint)index >= (uint)_size)
        {
            ThrowHelper.ArgumentOutOfRange_IndexMustBeLessThanCount();
        }

        int size = _size;
        if (index < size - 1)
        {
            Array.Copy(_items, index + 1, _items, index, size - index - 1);
        }
        _size = size - 1;
        _items[_size] = default!;
    }

    /// <summary>
    /// Removes the last item.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void RemoveLast()
    {
        if (_size > 0)
        {
            _size--;
            _items[_size] = default!;
        }
    }

    /// <summary>
    /// Removes the item at the specified index (no bounds check, caller must ensure valid index).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void RemoveAtUnsafe(int index)
    {
        int size = _size;
        if (index < size - 1)
        {
            Array.Copy(_items, index + 1, _items, index, size - index - 1);
        }
        _size = size - 1;
        _items[_size] = default!;
    }

    /// <summary>
    /// Finds and removes the first occurrence of an item.
    /// Uses EqualityComparer<T>.Default.
    /// </summary>
    public bool Remove(T item)
    {
        int index = IndexOf(item);
        if (index >= 0)
        {
            RemoveAt(index);
            return true;
        }
        return false;
    }

    /// <summary>
    /// Finds the index of the specified item.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int IndexOf(T item)
    {
        return IndexOf(item, 0, _size);
    }

    /// <summary>
    /// Finds the index of the specified item starting at a specific index.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int IndexOf(T item, int startIndex)
    {
        return IndexOf(item, startIndex, _size - startIndex);
    }

    /// <summary>
    /// Finds the index of the specified item within a range.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int IndexOf(T item, int startIndex, int count)
    {
        if ((uint)startIndex > (uint)_size)
        {
            ThrowHelper.ArgumentOutOfRange_IndexMustBeLessThanCount();
        }

        if (count < 0 || startIndex > _size - count)
        {
            ThrowHelper.ArgumentOutOfRange_Count();
        }

        // With 'where T : struct', T is always a value type - no boxing needed
        var span = new Span<T>(_items, startIndex, count);
        var index = span.IndexOf(item);
        return index >= 0 ? index + startIndex : -1;
    }

    /// <summary>
    /// Checks if the list contains the item.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Contains(T item)
    {
        return IndexOf(item) >= 0;
    }

    /// <summary>
    /// Sets the capacity to the actual number of elements.
    /// </summary>
    public void TrimExcess()
    {
        int size = _size;
        if (size < _items.Length)
        {
            if (size == 0)
            {
                _items = Array.Empty<T>();
            }
            else
            {
                var newItems = new T[size];
                Array.Copy(_items, 0, newItems, 0, size);
                _items = newItems;
            }
        }
    }

    /// <summary>
    /// Gets or sets an item at a specific index without bounds checking.
    /// Use with caution - no validation performed.
    /// </summary>
    public ref T ItemUnsafe(int index) => ref _items[index];

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void Grow(int capacity)
    {
        int newCapacity = GetNewCapacity(capacity);
        var newItems = new T[newCapacity];
        if (_size > 0)
        {
            Array.Copy(_items, 0, newItems, 0, _size);
        }
        _items = newItems;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int GetNewCapacity(int capacity)
    {
        Debug.Assert(_items.Length < capacity);

        int newCapacity = _items.Length == 0 ? DefaultCapacity : 2 * _items.Length;

        if ((uint)newCapacity > (uint)Array.MaxLength) newCapacity = Array.MaxLength;
        if (newCapacity < capacity) newCapacity = capacity;

        return newCapacity;
    }

    // Struct enumerator - NO BOXING!
    /// <summary>
    /// Struct enumerator for zero-allocation iteration.
    /// </summary>
    public struct Enumerator : IEnumerator<T>
    {
        private readonly T[] _array;
        private int _index;
        private readonly int _count;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal Enumerator(T[] array, int count)
        {
            _array = array;
            _count = count;
            _index = -1;
        }

        public ref T Current
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => ref _array[_index];
        }

        T IEnumerator<T>.Current => Current;

        object? IEnumerator.Current
        {
            get
            {
                if ((uint)_index >= (uint)_count)
                {
                    ThrowHelper.InvalidOperation_EnumOpCantHappen();
                }
                return Current;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool MoveNext()
        {
            return ++_index < _count;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Reset()
        {
            _index = -1;
        }

        public void Dispose() { }
    }

    /// <summary>
    /// Returns a struct enumerator for zero-allocation iteration.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Enumerator GetEnumerator()
    {
        return new Enumerator(_items, _size);
    }

    IEnumerator<T> IEnumerable<T>.GetEnumerator()
    {
        return GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }
}

/// <summary>
/// Helper class for throwing exceptions with consistent messages.
/// </summary>
internal static class ThrowHelper
{
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static void ArgumentOutOfRange_IndexMustBeLessThanCount()
    {
        throw new ArgumentOutOfRangeException("index", "Index was out of range. Must be non-negative and less than count.");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static void ArgumentOutOfRange_IndexMustBeLessThanSize()
    {
        throw new ArgumentOutOfRangeException("value", "Capacity must be at least the current size.");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static void ArgumentOutOfRange_NeedNonNegNum()
    {
        throw new ArgumentOutOfRangeException("capacity", "Non-negative number required.");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static void ArgumentOutOfRange_Count()
    {
        throw new ArgumentOutOfRangeException("count", "Count must be positive and within bounds.");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static void InvalidOperation_EnumOpCantHappen()
    {
        throw new InvalidOperationException("Enumeration has not started or has already finished.");
    }
}
