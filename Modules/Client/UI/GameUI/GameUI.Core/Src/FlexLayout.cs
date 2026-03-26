namespace Karpik.Engine.Client.UI.Core;

public struct FlexContainerStyle
{
    public FlexDirection Direction;
    public JustifyContent Justify;
    public AlignItems Align;
    public float Gap;
    public bool Wrap;
    public Padding Padding;
    public Margin Margin;

    public static FlexContainerStyle Default => new()
    {
        Direction = FlexDirection.Row,
        Justify = JustifyContent.Start,
        Align = AlignItems.Stretch,
        Gap = 0,
        Wrap = false,
        Padding = Padding.Zero,
        Margin = Margin.Zero
    };
}

public struct WidgetLayoutData
{
    public float PreferredWidth;
    public float PreferredHeight;
    public float MinWidth;
    public float MinHeight;
    public float MaxWidth;
    public float MaxHeight;

    public bool HasCustomWidth;
    public bool HasCustomHeight;

    public static WidgetLayoutData Default => new()
    {
        PreferredWidth = 0,
        PreferredHeight = 0,
        MinWidth = 0,
        MinHeight = 0,
        MaxWidth = float.MaxValue,
        MaxHeight = float.MaxValue,
        HasCustomWidth = false,
        HasCustomHeight = false
    };
}

public class LayoutEngine
{
    private readonly WidgetStorage _storage;
    private readonly Dictionary<int, WidgetLayoutData> _layoutData;

    public LayoutEngine(WidgetStorage storage)
    {
        _storage = storage;
        _layoutData = new Dictionary<int, WidgetLayoutData>();
    }

    public WidgetLayoutData GetLayoutData(int widgetIndex)
    {
        if (_layoutData.TryGetValue(widgetIndex, out var data))
            return data;
        return WidgetLayoutData.Default;
    }

    public void Invalidate(int widgetIndex)
    {
        if (!_storage.Has(widgetIndex))
            return;

        var tree = new WidgetTree(_storage);
        tree.Traverse(widgetIndex, i => _layoutData.Remove(i));
    }

    public void InvalidateSubtree(int widgetIndex)
    {
        Invalidate(widgetIndex);
    }

    public void CalculateLayout(int rootIndex)
    {
        if (!_storage.Has(rootIndex))
            return;

        CalculatePreferredSizes(rootIndex);
        CalculateFinalBounds(rootIndex);
    }

    private void CalculatePreferredSizes(int index)
    {
        if (!_storage.Has(index))
            return;

        var widget = _storage.GetWidget(index);

        if (!_layoutData.TryGetValue(index, out var layoutData))
        {
            layoutData = WidgetLayoutData.Default;
        }

        if (widget.HasChildren)
        {
            var flexStyle = GetFlexStyle(index);
            float totalWidth = 0;
            float totalHeight = 0;
            int childCount = 0;

            var childIndex = widget.FirstChildIndex;
            while (childIndex != UIWidget.NoChild)
            {
                CalculatePreferredSizes(childIndex);

                if (_layoutData.TryGetValue(childIndex, out var childData))
                {
                    if (flexStyle.Direction == FlexDirection.Row)
                    {
                        totalWidth += childData.PreferredWidth + flexStyle.Gap;
                        totalHeight = MathF.Max(totalHeight, childData.PreferredHeight);
                    }
                    else
                    {
                        totalWidth = MathF.Max(totalWidth, childData.PreferredWidth);
                        totalHeight += childData.PreferredHeight + flexStyle.Gap;
                    }
                }

                childCount++;
                childIndex = _storage.GetWidget(childIndex).NextSiblingIndex;
            }

            if (childCount > 0)
            {
                totalWidth -= flexStyle.Gap;
                totalHeight -= flexStyle.Gap;
            }

            layoutData.PreferredWidth = MathF.Max(layoutData.PreferredWidth, totalWidth + flexStyle.Padding.Horizontal);
            layoutData.PreferredHeight = MathF.Max(layoutData.PreferredHeight, totalHeight + flexStyle.Padding.Vertical);
        }

        _layoutData[index] = layoutData;
    }

    private void CalculateFinalBounds(int index)
    {
        if (!_storage.Has(index))
            return;

        ref var widget = ref _storage.Get(index);
        var bounds = widget.Bounds;

        if (!_layoutData.TryGetValue(index, out var layoutData))
        {
            layoutData = WidgetLayoutData.Default;
        }

        var flexStyle = GetFlexStyle(index);

        float x = bounds.X + flexStyle.Padding.Left;
        float y = bounds.Y + flexStyle.Padding.Top;
        float availableWidth = bounds.Width - flexStyle.Padding.Horizontal;
        float availableHeight = bounds.Height - flexStyle.Padding.Vertical;

        if (widget.HasChildren)
        {
            var children = GetChildrenList(widget.FirstChildIndex);
            float totalGap = (children.Count - 1) * flexStyle.Gap;

            float totalPreferredWidth = 0;
            float totalPreferredHeight = 0;

            foreach (var childIdx in children)
            {
                if (_layoutData.TryGetValue(childIdx, out var childData))
                {
                    totalPreferredWidth += childData.PreferredWidth;
                    totalPreferredHeight += childData.PreferredHeight;
                }
            }

            float startX = x;
            float startY = y;

            if (flexStyle.Justify == JustifyContent.Center)
            {
                float remainingX = availableWidth - totalPreferredWidth - totalGap;
                startX += remainingX / 2;
            }
            else if (flexStyle.Justify == JustifyContent.End)
            {
                float remainingX = availableWidth - totalPreferredWidth - totalGap;
                startX += remainingX;
            }
            else if (flexStyle.Justify == JustifyContent.SpaceBetween)
            {
                float remainingX = availableWidth - totalPreferredWidth - totalGap;
                float spacePerGap = children.Count > 1 ? remainingX / (children.Count - 1) : 0;
                float currentX = x;

                for (int i = 0; i < children.Count; i++)
                {
                    if (i > 0)
                        currentX += spacePerGap;

                    PlaceChild(children[i], currentX, startY, availableWidth, availableHeight, flexStyle);
                    currentX += _layoutData.TryGetValue(children[i], out var cd) ? cd.PreferredWidth + flexStyle.Gap : flexStyle.Gap;
                }
                return;
            }

            float currentX2 = startX;
            float currentY2 = startY;

            foreach (var childIdx in children)
            {
                PlaceChild(childIdx, currentX2, currentY2, availableWidth, availableHeight, flexStyle);

                if (flexStyle.Direction == FlexDirection.Row)
                {
                    float childWidth = _layoutData.TryGetValue(childIdx, out var cw) ? cw.PreferredWidth : 0;
                    currentX2 += childWidth + flexStyle.Gap;
                }
                else
                {
                    float childHeight = _layoutData.TryGetValue(childIdx, out var ch) ? ch.PreferredHeight : 0;
                    currentY2 += childHeight + flexStyle.Gap;
                }
            }
        }

        widget.IsDirty = false;
    }

    private void PlaceChild(int childIndex, float x, float y, float availableWidth, float availableHeight, FlexContainerStyle parentStyle)
    {
        if (!_storage.Has(childIndex))
            return;

        ref var child = ref _storage.Get(childIndex);

        if (!_layoutData.TryGetValue(childIndex, out var layoutData))
        {
            layoutData = WidgetLayoutData.Default;
        }

        float childWidth = layoutData.PreferredWidth;
        float childHeight = layoutData.PreferredHeight;

        if (parentStyle.Direction == FlexDirection.Row)
        {
            if (parentStyle.Align == AlignItems.Stretch)
                childHeight = availableHeight;
            else if (parentStyle.Align == AlignItems.Center)
                y += (availableHeight - childHeight) / 2;
            else if (parentStyle.Align == AlignItems.End)
                y += availableHeight - childHeight;
        }
        else
        {
            if (parentStyle.Align == AlignItems.Stretch)
                childWidth = availableWidth;
            else if (parentStyle.Align == AlignItems.Center)
                x += (availableWidth - childWidth) / 2;
            else if (parentStyle.Align == AlignItems.End)
                x += availableWidth - childWidth;
        }

        child.Bounds.X = x;
        child.Bounds.Y = y;
        child.Bounds.Width = childWidth;
        child.Bounds.Height = childHeight;

        CalculateFinalBounds(childIndex);
    }

    private List<int> GetChildrenList(int firstChildIndex)
    {
        var result = new List<int>();
        int current = firstChildIndex;

        while (current != UIWidget.NoChild)
        {
            result.Add(current);
            current = _storage.GetWidget(current).NextSiblingIndex;
        }

        return result;
    }

    private FlexContainerStyle GetFlexStyle(int index)
    {
        if (_storage.Has(index))
        {
            var widget = _storage.GetWidget(index);
            if (widget.Type == UiTypeId.Horizontal || widget.Type == UiTypeId.Vertical || widget.Type == UiTypeId.Window)
            {
                return FlexContainerStyle.Default;
            }
        }
        return FlexContainerStyle.Default;
    }

    public void SetPreferredSize(int index, float width, float height)
    {
        if (!_layoutData.TryGetValue(index, out var data))
        {
            data = WidgetLayoutData.Default;
        }

        data.PreferredWidth = width;
        data.PreferredHeight = height;
        data.HasCustomWidth = width > 0;
        data.HasCustomHeight = height > 0;

        _layoutData[index] = data;
    }
}
