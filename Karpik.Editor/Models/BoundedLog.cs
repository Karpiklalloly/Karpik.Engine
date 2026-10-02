namespace Karpik.Editor;

public sealed class BoundedLog
{
    private readonly int _capacity;
    private readonly Queue<string> _entries;

    public IReadOnlyList<string> Entries => _entries.ToArray();

    public BoundedLog(int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        _capacity = capacity;
        _entries = new Queue<string>(capacity);
    }

    public void Add(string entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (_entries.Count == _capacity)
        {
            _entries.Dequeue();
        }

        _entries.Enqueue(entry);
    }

    public void Clear() => _entries.Clear();
}
