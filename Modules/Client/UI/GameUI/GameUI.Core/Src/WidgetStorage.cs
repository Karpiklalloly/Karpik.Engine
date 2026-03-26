namespace Karpik.Engine.Client.UI.Core;

public class WidgetStorage
{
    private UIWidget[] _widgets;
    private int _count;
    private int _capacity;

    public int Count => _count;

    public WidgetStorage(int initialCapacity = 256)
    {
        _capacity = initialCapacity;
        _widgets = new UIWidget[_capacity];
        _count = 0;
    }

    public int Add(UIWidget widget)
    {
        if (_count >= _capacity)
        {
            Grow(_capacity * 2);
        }

        var index = _count;
        _widgets[index] = widget;
        _count++;
        return index;
    }

    public void Remove(int index)
    {
        if (index < 0 || index >= _count)
            return;

        ref var widget = ref _widgets[index];

        if (widget.HasParent)
        {
            UnlinkFromParent(index);
        }

        if (widget.HasChildren)
        {
            RemoveChildren(widget.FirstChildIndex);
        }

        for (int i = index; i < _count - 1; i++)
        {
            _widgets[i] = _widgets[i + 1];
            UpdateLinksAfterRemove(i, i + 1);
        }

        _count--;
    }

    public ref UIWidget Get(int index)
    {
        if (index < 0 || index >= _count)
            throw new IndexOutOfRangeException($"Widget index {index} is out of range. Count: {_count}");
        return ref _widgets[index];
    }

    public UIWidget GetWidget(int index)
    {
        if (index < 0 || index >= _count)
            throw new IndexOutOfRangeException($"Widget index {index} is out of range. Count: {_count}");
        return _widgets[index];
    }

    public bool Has(int index) => index >= 0 && index < _count;

    public void Clear()
    {
        _count = 0;
    }

    public Span<UIWidget> AsSpan() => _widgets.AsSpan(0, _count);

    public int AddChild(int parentIndex, UIWidget child)
    {
        if (!Has(parentIndex))
            return -1;

        ref var parent = ref Get(parentIndex);
        child.ParentIndex = parentIndex;

        if (!parent.HasChildren)
        {
            parent.FirstChildIndex = Add(child);
            ref var newChild = ref Get(parent.FirstChildIndex);
            newChild.PrevSiblingIndex = UIWidget.NoSibling;
            newChild.NextSiblingIndex = UIWidget.NoSibling;
            return parent.FirstChildIndex;
        }

        int lastChild = GetLastChild(parent.FirstChildIndex);
        int newIndex = Add(child);
        ref var newChildRef = ref Get(newIndex);
        newChildRef.PrevSiblingIndex = lastChild;
        Get(lastChild).NextSiblingIndex = newIndex;
        newChildRef.NextSiblingIndex = UIWidget.NoSibling;

        return newIndex;
    }

    private int GetLastChild(int firstChildIndex)
    {
        int current = firstChildIndex;
        while (Get(current).HasNextSibling)
        {
            current = Get(current).NextSiblingIndex;
        }
        return current;
    }

    private void UnlinkFromParent(int index)
    {
        ref var widget = ref Get(index);
        int parentIndex = widget.ParentIndex;
        ref var parent = ref Get(parentIndex);

        if (widget.HasPrevSibling)
        {
            Get(widget.PrevSiblingIndex).NextSiblingIndex = widget.NextSiblingIndex;
        }
        else
        {
            parent.FirstChildIndex = widget.NextSiblingIndex;
        }

        if (widget.HasNextSibling)
        {
            Get(widget.NextSiblingIndex).PrevSiblingIndex = widget.PrevSiblingIndex;
        }

        widget.ParentIndex = UIWidget.NoParent;
        widget.PrevSiblingIndex = UIWidget.NoSibling;
        widget.NextSiblingIndex = UIWidget.NoSibling;
    }

    private void RemoveChildren(int firstChildIndex)
    {
        int current = firstChildIndex;
        while (current != UIWidget.NoChild)
        {
            int next = Get(current).NextSiblingIndex;
            if (Get(current).HasChildren)
            {
                RemoveChildren(Get(current).FirstChildIndex);
            }
            Remove(current);
            current = next;
        }
    }

    private void UpdateLinksAfterRemove(int oldIndex, int newIndex)
    {
        ref var widget = ref Get(oldIndex);

        if (widget.HasParent)
        {
            if (Get(widget.ParentIndex).FirstChildIndex == newIndex)
            {
                Get(widget.ParentIndex).FirstChildIndex = oldIndex;
            }
        }

        if (widget.HasPrevSibling)
        {
            Get(widget.PrevSiblingIndex).NextSiblingIndex = oldIndex;
        }

        if (widget.HasNextSibling)
        {
            Get(widget.NextSiblingIndex).PrevSiblingIndex = oldIndex;
        }
    }

    private void Grow(int newCapacity)
    {
        var newArray = new UIWidget[newCapacity];
        Array.Copy(_widgets, newArray, _count);
        _widgets = newArray;
        _capacity = newCapacity;
    }
}
