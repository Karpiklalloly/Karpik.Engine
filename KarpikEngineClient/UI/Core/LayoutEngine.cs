using System.Numerics;
using Raylib_cs;

namespace Karpik.Engine.Client.UIToolkit;

public static class LayoutEngine
{
    public static void CalculateLayout(VisualElement root, StyleSheet? globalStyleSheet, Rectangle availableSpace)
    {
        root.Position = new Vector2(availableSpace.X, availableSpace.Y);
        root.Size = new Vector2(availableSpace.Width, availableSpace.Height);

        var style = StyleSheet.Combine(globalStyleSheet, root.StyleSheet);

        var old = root.StyleSheet;
        root.StyleSheet = style;
        CalculateLayoutRecursive(root, availableSpace);
        root.StyleSheet = old;
    }
    
    private static void CalculateLayoutRecursive(VisualElement element, Rectangle availableSpace)
    {
        if (!element.Visible) return;
        
        var computedStyle = element.ComputeStyle();
        
        element.Style.CopyFrom(computedStyle);
        
        // 1. Рассчитываем размеры элемента
        CalculateElementSize(element, computedStyle, availableSpace);
        
        // 2. Рассчитываем позицию для абсолютно позиционированных элементов
        if (computedStyle.Position == Position.Absolute)
        {
            CalculateAbsolutePosition(element, computedStyle, availableSpace);
        }
        
        // 3. Рассчитываем layout для детей
        if (element.Children.Count > 0)
        {
            CalculateChildrenLayout(element, computedStyle);
        }
    }
    
    private static void CalculateElementSize(VisualElement element, Style style, Rectangle availableSpace)
    {
        var size = element.Size;
        
        if (style.Width.HasValue) size.X = style.Width.Value;
        else if (element.Parent == null) size.X = availableSpace.Width;
            
        if (style.Height.HasValue) size.Y = style.Height.Value;
        else if (element.Parent == null) size.Y = availableSpace.Height;
        
        if (style.MinWidth.HasValue) size.X = Math.Max(size.X, style.MinWidth.Value);
        if (style.MaxWidth.HasValue) size.X = Math.Min(size.X, style.MaxWidth.Value);
        if (style.MinHeight.HasValue) size.Y = Math.Max(size.Y, style.MinHeight.Value);
        if (style.MaxHeight.HasValue) size.Y = Math.Min(size.Y, style.MaxHeight.Value);
        
        element.Size = size;
    }
    
    private static void CalculateAbsolutePosition(VisualElement element, Style style, Rectangle availableSpace)
    {
        var position = element.Position;
        
        if (style.Left.HasValue)
            position.X = availableSpace.X + style.Left.Value;
        else if (style.Right.HasValue)
            position.X = availableSpace.X + availableSpace.Width - element.Size.X - style.Right.Value;
            
        if (style.Top.HasValue)
            position.Y = availableSpace.Y + style.Top.Value;
        else if (style.Bottom.HasValue)
            position.Y = availableSpace.Y + availableSpace.Height - element.Size.Y - style.Bottom.Value;
        
        element.Position = position;
    }
    
    private static void CalculateChildrenLayout(VisualElement parent, Style parentStyle)
    {
        if (parent.Children.Count == 0) return;
        
        var contentArea = new Rectangle(
            parent.Position.X + parentStyle.Padding.Left,
            parent.Position.Y + parentStyle.Padding.Top,
            parent.Size.X - parentStyle.Padding.Left - parentStyle.Padding.Right,
            parent.Size.Y - parentStyle.Padding.Top - parentStyle.Padding.Bottom
        );
        
        var visibleChildren = parent.Children.Where(static c => c.Visible).ToList();
        if (visibleChildren.Count == 0) return;
        
        switch (parentStyle.FlexDirection)
        {
            case FlexDirection.Column or FlexDirection.ColumnReverse:
                LayoutColumn(visibleChildren, parentStyle, contentArea);
                break;
            case FlexDirection.Row or FlexDirection.RowReverse:
                LayoutRow(visibleChildren, parentStyle, contentArea);
                break;
        }

        // Рекурсивно рассчитываем layout для детей
        foreach (var child in visibleChildren)
        {
            var childStyle = child.ComputeStyle();
            Rectangle childContentArea;
            
            if (childStyle.Position == Position.Absolute)
            {
                // Абсолютно позиционированные элементы позиционируются относительно корня
                childContentArea = GetRootAvailableSpace(parent);
            }
            else
            {
                childContentArea = new Rectangle(
                    child.Position.X, child.Position.Y,
                    child.Size.X, child.Size.Y
                );
            }
            
            CalculateLayoutRecursive(child, childContentArea);
        }
    }
    
    private static void LayoutColumn(List<VisualElement> children, Style parentStyle, Rectangle contentArea)
    {
        float totalHeight = 0;
        float totalFlexGrow = 0;
        var childInfos = new List<ChildInfo>();
        
        // Собираем информацию о детях
        foreach (var child in children)
        {
            var childStyle = child.ComputeStyle();
            var info = new ChildInfo { Element = child, Style = childStyle };
            
            CalculateElementSize(child, childStyle, contentArea);
            
            totalHeight += childStyle.Margin.Top + childStyle.Margin.Bottom;
            
            if (childStyle.FlexGrow > 0)
            {
                totalFlexGrow += childStyle.FlexGrow;
                info.IsFlexItem = true;
            }
            else
            {
                totalHeight += child.Size.Y;
            }
            
            childInfos.Add(info);
        }
        
        // Позиционируем элементы
        float currentY = contentArea.Y;
        float remainingHeight = Math.Max(0, contentArea.Height - totalHeight);
        
        switch (parentStyle.JustifyContent)
        {
            case JustifyContent.Center:
                currentY += remainingHeight / 2;
                break;
            case JustifyContent.FlexEnd:
                currentY += remainingHeight;
                break;
            case JustifyContent.FlexStart:
                break;
            case JustifyContent.SpaceBetween:
            case JustifyContent.SpaceAround:
            case JustifyContent.SpaceEvenly:
            default:
                throw new ArgumentOutOfRangeException(nameof(parentStyle.JustifyContent));
        }
        
        foreach (var info in childInfos)
        {
            var child = info.Element;
            var childStyle = info.Style;
            
            currentY += childStyle.Margin.Top;
            
            float childX = contentArea.X + childStyle.Margin.Left;
            switch (parentStyle.AlignItems)
            {
                case AlignItems.Center:
                    childX = contentArea.X + (contentArea.Width - child.Size.X) / 2;
                    break;
                case AlignItems.FlexEnd:
                    childX = contentArea.X + contentArea.Width - child.Size.X - childStyle.Margin.Right;
                    break;
                case AlignItems.Stretch when !childStyle.Width.HasValue:
                    child.Size = new Vector2(contentArea.Width - childStyle.Margin.Left - childStyle.Margin.Right, child.Size.Y);
                    break;
                case AlignItems.FlexStart:
                    break;
                case AlignItems.Baseline:
                default:
                    throw new ArgumentOutOfRangeException(nameof(parentStyle.AlignItems));
            }
            
            // Устанавливаем позицию
            child.Position = new Vector2(childX, currentY);
            
            // Рассчитываем высоту для flex элементов
            if (info.IsFlexItem && totalFlexGrow > 0)
            {
                float flexHeight = (childStyle.FlexGrow / totalFlexGrow) * remainingHeight;
                child.Size = new Vector2(child.Size.X, flexHeight);
                currentY += flexHeight;
            }
            else
            {
                currentY += child.Size.Y;
            }
            
            currentY += childStyle.Margin.Bottom;
        }
    }
    
    private static void LayoutRow(List<VisualElement> children, Style parentStyle, Rectangle contentArea)
    {
        float totalWidth = 0;
        float totalFlexGrow = 0;
        var childInfos = new List<ChildInfo>();
        
        // Собираем информацию о детях
        foreach (var child in children)
        {
            var childStyle = child.ComputeStyle();
            var info = new ChildInfo { Element = child, Style = childStyle };
            
            CalculateElementSize(child, childStyle, contentArea);
            
            totalWidth += childStyle.Margin.Left + childStyle.Margin.Right;
            
            if (childStyle.FlexGrow > 0)
            {
                totalFlexGrow += childStyle.FlexGrow;
                info.IsFlexItem = true;
            }
            else
            {
                totalWidth += child.Size.X;
            }
            
            childInfos.Add(info);
        }
        
        // Позиционируем элементы
        float currentX = contentArea.X;
        float remainingWidth = Math.Max(0, contentArea.Width - totalWidth);

        switch (parentStyle.JustifyContent)
        {
            case JustifyContent.Center:
                currentX += remainingWidth / 2;
                break;
            case JustifyContent.FlexEnd:
                currentX += remainingWidth;
                break;
            case JustifyContent.FlexStart:
                break;
            case JustifyContent.SpaceBetween:
            case JustifyContent.SpaceAround:
            case JustifyContent.SpaceEvenly:
            default:
                throw new ArgumentOutOfRangeException(nameof(parentStyle.JustifyContent));
        }
        
        foreach (var info in childInfos)
        {
            var child = info.Element;
            var childStyle = info.Style;
            
            currentX += childStyle.Margin.Left;
            
            float childY = contentArea.Y + childStyle.Margin.Top;
            switch (parentStyle.AlignItems)
            {
                case AlignItems.Center:
                    childY = contentArea.Y + (contentArea.Height - child.Size.Y) / 2;
                    break;
                case AlignItems.FlexEnd:
                    childY = contentArea.Y + contentArea.Height - child.Size.Y - childStyle.Margin.Bottom;
                    break;
                case AlignItems.Stretch when !childStyle.Height.HasValue:
                    child.Size = new Vector2(child.Size.X, contentArea.Height - childStyle.Margin.Top - childStyle.Margin.Bottom);
                    break;
                case AlignItems.FlexStart:
                    break;
                case AlignItems.Baseline:
                default:
                    throw new ArgumentOutOfRangeException(nameof(parentStyle.AlignItems));
            }
            
            child.Position = new Vector2(currentX, childY);
            
            // Рассчитываем ширину для flex элементов
            if (info.IsFlexItem && totalFlexGrow > 0)
            {
                float flexWidth = (childStyle.FlexGrow / totalFlexGrow) * remainingWidth;
                child.Size = new Vector2(flexWidth, child.Size.Y);
                currentX += flexWidth;
            }
            else
            {
                currentX += child.Size.X;
            }
            
            currentX += childStyle.Margin.Right;
        }
    }
    
    private class ChildInfo
    {
        public VisualElement Element { get; set; } = null!;
        public Style Style { get; set; } = null!;
        public bool IsFlexItem { get; set; }
    }
    
    private static Rectangle GetRootAvailableSpace(VisualElement element)
    {
        // Поднимаемся до корневого элемента
        var root = element;
        while (root.Parent != null)
            root = root.Parent;
            
        return new Rectangle(0, 0, root.Size.X, root.Size.Y);
    }
}