namespace Karpik.Engine.Client.UI.Core;

public class WidgetTree
{
    private readonly WidgetStorage _storage;

    public WidgetTree(WidgetStorage storage)
    {
        _storage = storage;
    }

    public List<int> GetChildren(int parentIndex)
    {
        var result = new List<int>();
        
        if (!_storage.Has(parentIndex))
            return result;

        var widget = _storage.GetWidget(parentIndex);
        if (!widget.HasChildren)
            return result;

        var childIndex = widget.FirstChildIndex;
        while (childIndex != UIWidget.NoChild)
        {
            result.Add(childIndex);
            childIndex = _storage.GetWidget(childIndex).NextSiblingIndex;
        }
        
        return result;
    }

    public List<int> GetDescendants(int rootIndex)
    {
        var result = new List<int>();
        var stack = new FastStack<int>(64);
        stack.Push(rootIndex);

        while (stack.TryPop(out int index))
        {
            if (!_storage.Has(index))
                continue;

            result.Add(index);
            
            var widget = _storage.GetWidget(index);

            if (widget.HasChildren)
            {
                var childIndex = widget.FirstChildIndex;
                while (childIndex != UIWidget.NoChild)
                {
                    stack.Push(childIndex);
                    childIndex = _storage.GetWidget(childIndex).NextSiblingIndex;
                }
            }
        }
        
        return result;
    }

    public void Traverse(int rootIndex, Action<int> visit)
    {
        if (!_storage.Has(rootIndex))
            return;

        var stack = new FastStack<int>(64);
        stack.Push(rootIndex);

        while (stack.TryPop(out int index))
        {
            visit(index);

            var widget = _storage.GetWidget(index);
            if (widget.HasChildren)
            {
                var childIndex = widget.FirstChildIndex;
                var children = new List<int>();

                while (childIndex != UIWidget.NoChild)
                {
                    children.Add(childIndex);
                    childIndex = _storage.GetWidget(childIndex).NextSiblingIndex;
                }

                for (int i = children.Count - 1; i >= 0; i--)
                {
                    stack.Push(children[i]);
                }
            }
        }
    }

    public void TraverseDFS(int rootIndex, Action<int> visit)
    {
        if (!_storage.Has(rootIndex))
            return;

        visit(rootIndex);

        var widget = _storage.GetWidget(rootIndex);
        if (widget.HasChildren)
        {
            var childIndex = widget.FirstChildIndex;
            while (childIndex != UIWidget.NoChild)
            {
                TraverseDFS(childIndex, visit);
                childIndex = _storage.GetWidget(childIndex).NextSiblingIndex;
            }
        }
    }

    public int FindWidgetAt(int rootIndex, Vector2 position)
    {
        var widgetsByZ = new List<(int index, int zIndex)>();
        CollectWidgetsByZ(rootIndex, widgetsByZ);
        widgetsByZ.Sort((a, b) => b.zIndex.CompareTo(a.zIndex));

        foreach (var (index, _) in widgetsByZ)
        {
            var widget = _storage.GetWidget(index);
            if (!widget.IsVisible)
                continue;

            if (widget.Bounds.Contains(position))
                return index;
        }

        return -1;
    }

    private void CollectWidgetsByZ(int index, List<(int, int)> result)
    {
        if (!_storage.Has(index))
            return;

        var widget = _storage.GetWidget(index);
        result.Add((index, widget.ZIndex));

        if (widget.HasChildren)
        {
            var childIndex = widget.FirstChildIndex;
            while (childIndex != UIWidget.NoChild)
            {
                CollectWidgetsByZ(childIndex, result);
                childIndex = _storage.GetWidget(childIndex).NextSiblingIndex;
            }
        }
    }

    public int GetDepth(int index)
    {
        int depth = 0;
        var widget = _storage.GetWidget(index);

        while (widget.HasParent)
        {
            depth++;
            widget = _storage.GetWidget(widget.ParentIndex);
        }

        return depth;
    }
}

internal class FastStack<T>
{
    private T[] _array;
    private int _count;

    public FastStack(int capacity)
    {
        _array = new T[capacity];
        _count = 0;
    }

    public void Push(T item)
    {
        if (_count >= _array.Length)
        {
            var newArray = new T[_array.Length * 2];
            Array.Copy(_array, newArray, _count);
            _array = newArray;
        }
        _array[_count++] = item;
    }

    public bool TryPop(out T item)
    {
        if (_count > 0)
        {
            item = _array[--_count];
            return true;
        }
        item = default!;
        return false;
    }

    public int Count => _count;
}
