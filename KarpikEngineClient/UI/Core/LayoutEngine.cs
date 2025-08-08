using System.Numerics;
using Raylib_cs;

namespace Karpik.Engine.Client.UIToolkit.Core;

public static class LayoutEngine
{
    public static void CalculateLayout(VisualElement root, StyleSheet styleSheet, Rectangle availableSpace)
    {
        root.Position = new Vector2(availableSpace.X, availableSpace.Y);
        root.Size = new Vector2(availableSpace.Width, availableSpace.Height);
        
        CalculateLayoutRecursive(root, styleSheet, availableSpace);
    }
    
    private static void CalculateLayoutRecursive(VisualElement element, StyleSheet styleSheet, Rectangle availableSpace)
    {
        if (!element.Visible) return;
        
        var computedStyle = styleSheet.ComputeStyle(element);
        
        // Применяем вычисленные стили к элементу
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
            CalculateChildrenLayout(element, computedStyle, styleSheet);
        }
    }
    
    private static void CalculateElementSize(VisualElement element, Style style, Rectangle availableSpace)
    {
        var size = element.Size;
        
        // Применяем заданные размеры
        if (style.Width.HasValue)
            size.X = style.Width.Value;
        else if (element.Parent == null) // Корневой элемент
            size.X = availableSpace.Width;
            
        if (style.Height.HasValue)
            size.Y = style.Height.Value;
        else if (element.Parent == null) // Корневой элемент
            size.Y = availableSpace.Height;
        
        // Применяем ограничения
        if (style.MinWidth.HasValue)
            size.X = Math.Max(size.X, style.MinWidth.Value);
        if (style.MaxWidth.HasValue)
            size.X = Math.Min(size.X, style.MaxWidth.Value);
        if (style.MinHeight.HasValue)
            size.Y = Math.Max(size.Y, style.MinHeight.Value);
        if (style.MaxHeight.HasValue)
            size.Y = Math.Min(size.Y, style.MaxHeight.Value);
        
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
        
        // Отладка
        Console.WriteLine($"Absolute element {element.Name}: availableSpace={availableSpace}, calculated position=({position.X}, {position.Y})");
        
        element.Position = position;
    }
    
    private static void CalculateChildrenLayout(VisualElement parent, Style parentStyle, StyleSheet styleSheet)
    {
        if (parent.Children.Count == 0) return;
        
        // Рассчитываем доступное пространство с учетом padding
        var contentArea = new Rectangle(
            parent.Position.X + parentStyle.Padding.Left,
            parent.Position.Y + parentStyle.Padding.Top,
            parent.Size.X - parentStyle.Padding.Left - parentStyle.Padding.Right,
            parent.Size.Y - parentStyle.Padding.Top - parentStyle.Padding.Bottom
        );
        
        // Фильтруем видимые дети
        var visibleChildren = parent.Children.Where(c => c.Visible).ToList();
        if (visibleChildren.Count == 0) return;
        
        // Рассчитываем layout в зависимости от направления
        if (parentStyle.FlexDirection == FlexDirection.Column || 
            parentStyle.FlexDirection == FlexDirection.ColumnReverse)
        {
            LayoutColumn(visibleChildren, parentStyle, styleSheet, contentArea);
        }
        else
        {
            LayoutRow(visibleChildren, parentStyle, styleSheet, contentArea);
        }
        
        // Рекурсивно рассчитываем layout для детей
        foreach (var child in visibleChildren)
        {
            // Для абсолютно позиционированных элементов используем весь экран
            var childStyle = styleSheet.ComputeStyle(child);
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
            
            CalculateLayoutRecursive(child, styleSheet, childContentArea);
        }
    }
    
    private static void LayoutColumn(List<VisualElement> children, Style parentStyle, StyleSheet styleSheet, Rectangle contentArea)
    {
        float totalHeight = 0;
        float totalFlexGrow = 0;
        var childInfos = new List<ChildInfo>();
        
        // Первый проход: собираем информацию о детях
        foreach (var child in children)
        {
            var childStyle = styleSheet.ComputeStyle(child);
            var info = new ChildInfo { Element = child, Style = childStyle };
            
            // Рассчитываем размеры
            CalculateElementSize(child, childStyle, contentArea);
            
            // Добавляем margin
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
        
        // Второй проход: позиционируем элементы
        float currentY = contentArea.Y;
        float remainingHeight = Math.Max(0, contentArea.Height - totalHeight);
        
        // Применяем justify-content
        if (parentStyle.JustifyContent == JustifyContent.Center)
            currentY += remainingHeight / 2;
        else if (parentStyle.JustifyContent == JustifyContent.FlexEnd)
            currentY += remainingHeight;
        
        foreach (var info in childInfos)
        {
            var child = info.Element;
            var childStyle = info.Style;
            
            // Добавляем margin-top
            currentY += childStyle.Margin.Top;
            
            // Рассчитываем X позицию с учетом align-items
            float childX = contentArea.X + childStyle.Margin.Left;
            if (parentStyle.AlignItems == AlignItems.Center)
                childX = contentArea.X + (contentArea.Width - child.Size.X) / 2;
            else if (parentStyle.AlignItems == AlignItems.FlexEnd)
                childX = contentArea.X + contentArea.Width - child.Size.X - childStyle.Margin.Right;
            else if (parentStyle.AlignItems == AlignItems.Stretch && !childStyle.Width.HasValue)
                child.Size = new Vector2(contentArea.Width - childStyle.Margin.Left - childStyle.Margin.Right, child.Size.Y);
            
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
            
            // Добавляем margin-bottom
            currentY += childStyle.Margin.Bottom;
        }
    }
    
    private static void LayoutRow(List<VisualElement> children, Style parentStyle, StyleSheet styleSheet, Rectangle contentArea)
    {
        float totalWidth = 0;
        float totalFlexGrow = 0;
        var childInfos = new List<ChildInfo>();
        
        // Первый проход: собираем информацию о детях
        foreach (var child in children)
        {
            var childStyle = styleSheet.ComputeStyle(child);
            var info = new ChildInfo { Element = child, Style = childStyle };
            
            // Рассчитываем размеры
            CalculateElementSize(child, childStyle, contentArea);
            
            // Добавляем margin
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
        
        // Второй проход: позиционируем элементы
        float currentX = contentArea.X;
        float remainingWidth = Math.Max(0, contentArea.Width - totalWidth);
        
        // Применяем justify-content
        if (parentStyle.JustifyContent == JustifyContent.Center)
            currentX += remainingWidth / 2;
        else if (parentStyle.JustifyContent == JustifyContent.FlexEnd)
            currentX += remainingWidth;
        
        foreach (var info in childInfos)
        {
            var child = info.Element;
            var childStyle = info.Style;
            
            // Добавляем margin-left
            currentX += childStyle.Margin.Left;
            
            // Рассчитываем Y позицию с учетом align-items
            float childY = contentArea.Y + childStyle.Margin.Top;
            if (parentStyle.AlignItems == AlignItems.Center)
                childY = contentArea.Y + (contentArea.Height - child.Size.Y) / 2;
            else if (parentStyle.AlignItems == AlignItems.FlexEnd)
                childY = contentArea.Y + contentArea.Height - child.Size.Y - childStyle.Margin.Bottom;
            else if (parentStyle.AlignItems == AlignItems.Stretch && !childStyle.Height.HasValue)
                child.Size = new Vector2(child.Size.X, contentArea.Height - childStyle.Margin.Top - childStyle.Margin.Bottom);
            
            // Устанавливаем позицию
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
            
            // Добавляем margin-right
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