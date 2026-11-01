namespace Karpik.Content.Runtime;

/// <summary>Single-owner, preallocated unique claims. Capacity changes belong to warm-up only.</summary>
public sealed class ContentResourcePins
{
    private ContentResourceLifetime?[] _table;
    private ContentResourceLifetime?[] _owners;
    private int _count;
    public ContentResourcePins(int capacity = 256)
    {
        int size = 2;
        while (size < checked(capacity * 2)) size = checked(size * 2);
        _table = new ContentResourceLifetime[size];
        _owners = new ContentResourceLifetime[size / 2];
    }
    public void EnsureCapacity(int capacity)
    {
        if (capacity <= _owners.Length) return;
        if (_count != 0) throw new InvalidOperationException("Resize claims only during warm-up with an empty buffer.");
        int size = _table.Length;
        while (size < checked(capacity * 2)) size = checked(size * 2);
        _table = new ContentResourceLifetime[size]; _owners = new ContentResourceLifetime[size / 2];
    }
    public bool TryAdd(ContentResourceLifetime owner) => Add(owner, false);
    private bool Add(ContentResourceLifetime owner, bool transfer)
    {
        int index = owner.IdentityHash & (_table.Length - 1);
        while (_table[index] != null)
        {
            if (ReferenceEquals(_table[index], owner)) return true;
            index = (index + 1) & (_table.Length - 1);
        }
        if (_count == _owners.Length) throw new InvalidOperationException("Resource claim capacity exceeded. Increase capacity during warm-up.");
        if (transfer) owner.RetainExisting(); else if (!owner.TryRetain()) return false;
        _table[index] = owner; _owners[_count++] = owner;
        return true;
    }
    public void RetainInto(ContentResourcePins destination)
    {
        for (int i = 0; i < _count; i++) destination.Add(_owners[i]!, true);
    }
    public void Clear()
    {
        for (int i = 0; i < _count; i++) { _owners[i]!.Release(); _owners[i] = null; }
        Array.Clear(_table); _count = 0;
    }
}
