using System.Numerics;
using Raylib_cs;

namespace Karpik.Engine.Client.UIToolkit;

public static class LayoutEngine
{
    public static void CalculateLayout(VisualElement root, Rectangle availableSpace)
    {
        // Устанавливаем позицию корневого элемента
        root.Position = new Vector2(availableSpace.X, availableSpace.Y);
        
        CalculateLayoutRecursive(root, availableSpace);
    }
    
    private static void CalculateLayoutRecursive(VisualElement element, Rectangle availableSpace)
    {
        if (!element.Visible) return;
        
        var computedStyle = element.GetComputedStyle();
        
        // 1. Рассчитываем размеры элемента с учетом всех стилей
        CalculateElementSize(element, computedStyle, availableSpace);
        
        // 2. Рассчитываем позицию только для абсолютно позиционированных элементов
        // Для обычных элементов позиция устанавливается родительским контейнером
        if (computedStyle.Position.IsSet && computedStyle.Position.Value == PositionType.Absolute)
        {
            CalculateElementPosition(element, computedStyle, availableSpace);
        }
        
        // 3. Рассчитываем layout для детей с учетом padding
        if (element.Children.Count > 0)
        {
            CalculateChildrenLayout(element, computedStyle);
        }
        
        // 4. Рекурсивно рассчитываем layout для всех детей
        // Используем внутреннее пространство элемента (с учетом padding)
        var paddingLeft = computedStyle.PaddingLeft.IsSet ? computedStyle.PaddingLeft.Value : 0;
        var paddingRight = computedStyle.PaddingRight.IsSet ? computedStyle.PaddingRight.Value : 0;
        var paddingTop = computedStyle.PaddingTop.IsSet ? computedStyle.PaddingTop.Value : 0;
        var paddingBottom = computedStyle.PaddingBottom.IsSet ? computedStyle.PaddingBottom.Value : 0;
        
        var childSpace = new Rectangle(
            element.Position.X + paddingLeft,
            element.Position.Y + paddingTop,
            element.Size.X - paddingLeft - paddingRight,
            element.Size.Y - paddingTop - paddingBottom
        );
        
        foreach (var child in element.Children)
        {
            CalculateLayoutRecursive(child, childSpace);
        }
    }
    
    private static void CalculateElementSize(VisualElement element, Style style, Rectangle availableSpace)
    {
        Vector2 size = element.Size;
        
        // Применяем базовые размеры
        if (style.Width.IsSet)
        {
            size.X = style.Width.Value;
        }
        else
        {
            // По умолчанию - занимаем доступное пространство
            size.X = availableSpace.Width;
        }
        
        if (style.Height.IsSet)
        {
            size.Y = style.Height.Value;
        }
        else
        {
            size.Y = availableSpace.Height;
        }
        
        // Применяем ограничения
        ApplySizeConstraints(ref size, style);
        
        element.Size = size;
    }
    
    private static void ApplySizeConstraints(ref Vector2 size, Style style)
    {
        // Min/Max ограничения применяются всегда
        if (style.MinWidth.IsSet)
            size.X = Math.Max(size.X, style.MinWidth.Value);
        if (style.MaxWidth.IsSet)
            size.X = Math.Min(size.X, style.MaxWidth.Value);
        if (style.MinHeight.IsSet)
            size.Y = Math.Max(size.Y, style.MinHeight.Value);
        if (style.MaxHeight.IsSet)
            size.Y = Math.Min(size.Y, style.MaxHeight.Value);
    }
    
    private static void CalculateElementPosition(VisualElement element, Style style, Rectangle availableSpace)
    {
        Vector2 position = Vector2.Zero; // Начинаем с нуля, а не с текущей позиции
    
        // Обработка разных типов позиционирования
        if (style.Position.IsSet && style.Position.Value == PositionType.Absolute)
        {
            // Абсолютное позиционирование
            if (style.Left.IsSet)
                position.X = style.Left.Value;
            else if (style.Right.IsSet)
                position.X = availableSpace.Width - element.Size.X - style.Right.Value;
            
            if (style.Top.IsSet)
                position.Y = style.Top.Value;
            else if (style.Bottom.IsSet)
                position.Y = availableSpace.Height - element.Size.Y - style.Bottom.Value;
        }
        else
        {
            // Относительное позиционирование - начинаем с позиции доступного пространства
            position.X = availableSpace.X;
            position.Y = availableSpace.Y;
            
            // Добавляем margin
            if (style.MarginLeft.IsSet)
                position.X += style.MarginLeft.Value;
            if (style.MarginTop.IsSet)
                position.Y += style.MarginTop.Value;
            else if (style.Margin.IsSet)
            {
                position.X += style.Margin.Value;
                position.Y += style.Margin.Value;
            }
        }
    
        element.Position = position;
    }
    
    private static void CalculateChildrenLayout(VisualElement parent, Style parentStyle)
    {
        // Учитываем padding родителя
        float paddingLeft = parentStyle.PaddingLeft.IsSet ? parentStyle.PaddingLeft.Value : 0;
        float paddingRight = parentStyle.PaddingRight.IsSet ? parentStyle.PaddingRight.Value : 0;
        float paddingTop = parentStyle.PaddingTop.IsSet ? parentStyle.PaddingTop.Value : 0;
        float paddingBottom = parentStyle.PaddingBottom.IsSet ? parentStyle.PaddingBottom.Value : 0;
        
        // Доступное пространство для детей с учетом padding
        Rectangle childSpace = new Rectangle(
            parent.Position.X + paddingLeft,
            parent.Position.Y + paddingTop,
            parent.Size.X - paddingLeft - paddingRight,
            parent.Size.Y - paddingTop - paddingBottom
        );
        
        // Определяем направление flex
        var flexDirection = parentStyle.FlexDirection.IsSet ? 
                           parentStyle.FlexDirection.Value : FlexDirection.Column;
        
        // Определяем выравнивание
        var justifyContent = parentStyle.JustifyContent.IsSet ? 
                            parentStyle.JustifyContent.Value : Justify.FlexStart;
        
        var alignItems = parentStyle.AlignItems.IsSet ? 
                        parentStyle.AlignItems.Value : Align.Stretch;
        
        // Рассчитываем layout в зависимости от направления
        if (flexDirection == FlexDirection.Column || flexDirection == FlexDirection.ColumnReverse)
        {
            LayoutColumn(parent, childSpace, justifyContent, alignItems);
        }
        else
        {
            LayoutRow(parent, childSpace, justifyContent, alignItems);
        }
    }
    
    private static void LayoutColumn(VisualElement parent, Rectangle availableSpace, 
                                   Justify justifyContent, Align alignItems)
    {
        float totalHeight = 0;
        float totalFlexGrow = 0;
        var childrenInfo = new List<ChildLayoutInfo>();
        
        // Первый проход: собираем информацию о детях
        foreach (var child in parent.Children)
        {
            if (!child.Visible) continue;
            
            var childStyle = child.GetComputedStyle();
            var info = new ChildLayoutInfo { Element = child, Style = childStyle };
            
            if (childStyle.FlexGrow.IsSet && childStyle.FlexGrow.Value > 0)
            {
                info.IsFlexItem = true;
                totalFlexGrow += childStyle.FlexGrow.Value;
            }
            else
            {
                info.IsFlexItem = false;
                // Не пересчитываем размер здесь - он уже рассчитан в CalculateLayoutRecursive
                info.Size = child.Size;
                totalHeight += child.Size.Y;
            }
            
            childrenInfo.Add(info);
        }
        
        // Применяем gap, если есть
        float gap = 0; // Пока не реализован
        totalHeight += Math.Max(0, childrenInfo.Count - 1) * gap;
        
        // Второй проход: рассчитываем позиции
        float currentY = availableSpace.Y;
        float remainingHeight = availableSpace.Height - totalHeight;
        
        // Выравнивание по justifyContent
        if (justifyContent == Justify.Center)
        {
            currentY += remainingHeight / 2;
        }
        else if (justifyContent == Justify.FlexEnd)
        {
            currentY += remainingHeight;
        }
        else if (justifyContent == Justify.SpaceBetween && childrenInfo.Count > 1)
        {
            // Будет реализовано позже
        }
        
        foreach (var info in childrenInfo)
        {
            if (!info.Element.Visible) continue;
            
            // Устанавливаем X позицию с учетом alignItems
            AlignChildHorizontally(info.Element, info.Style, availableSpace, alignItems);
            
            // Добавляем margin к текущей позиции
            float marginTop = info.Style.MarginTop.IsSet ? info.Style.MarginTop.Value : 
                             (info.Style.Margin.IsSet ? info.Style.Margin.Value : 0);
            float marginLeft = info.Style.MarginLeft.IsSet ? info.Style.MarginLeft.Value : 
                              (info.Style.Margin.IsSet ? info.Style.Margin.Value : 0);
            
            currentY += marginTop;
            
            // Устанавливаем Y позицию и размеры
            if (info.IsFlexItem && totalFlexGrow > 0)
            {
                float flexHeight = (info.Style.FlexGrow.Value / totalFlexGrow) * remainingHeight;
                info.Element.Size = new Vector2(info.Element.Size.X, flexHeight);
                info.Element.Position = new Vector2(availableSpace.X + marginLeft, currentY);
                currentY += flexHeight + gap;
            }
            else
            {
                info.Element.Position = new Vector2(availableSpace.X + marginLeft, currentY);
                currentY += info.Element.Size.Y + gap;
            }
        }
    }
    
    private static void LayoutRow(VisualElement parent, Rectangle availableSpace,
                                Justify justifyContent, Align alignItems)
    {
        float totalWidth = 0;
        float totalFlexGrow = 0;
        var childrenInfo = new List<ChildLayoutInfo>();
        
        // Первый проход: собираем информацию о детях
        foreach (var child in parent.Children)
        {
            if (!child.Visible) continue;
            
            var childStyle = child.GetComputedStyle();
            var info = new ChildLayoutInfo { Element = child, Style = childStyle };
            
            if (childStyle.FlexGrow.IsSet && childStyle.FlexGrow.Value > 0)
            {
                info.IsFlexItem = true;
                totalFlexGrow += childStyle.FlexGrow.Value;
            }
            else
            {
                info.IsFlexItem = false;
                // Не пересчитываем размер здесь - он уже рассчитан в CalculateLayoutRecursive
                info.Size = child.Size;
                totalWidth += child.Size.X;
            }
            
            childrenInfo.Add(info);
        }
        
        // Применяем gap, если есть
        float gap = 0; // Пока не реализован
        totalWidth += Math.Max(0, childrenInfo.Count - 1) * gap;
        
        // Второй проход: рассчитываем позиции
        float currentX = availableSpace.X;
        float remainingWidth = availableSpace.Width - totalWidth;
        
        // Выравнивание по justifyContent
        if (justifyContent == Justify.Center)
        {
            currentX += remainingWidth / 2;
        }
        else if (justifyContent == Justify.FlexEnd)
        {
            currentX += remainingWidth;
        }
        
        foreach (var info in childrenInfo)
        {
            if (!info.Element.Visible) continue;
            
            // Устанавливаем Y позицию с учетом alignItems
            AlignChildVertically(info.Element, info.Style, availableSpace, alignItems);
            
            // Добавляем margin к текущей позиции
            float marginTop = info.Style.MarginTop.IsSet ? info.Style.MarginTop.Value : 
                             (info.Style.Margin.IsSet ? info.Style.Margin.Value : 0);
            float marginLeft = info.Style.MarginLeft.IsSet ? info.Style.MarginLeft.Value : 
                              (info.Style.Margin.IsSet ? info.Style.Margin.Value : 0);
            
            currentX += marginLeft;
            
            // Устанавливаем X позицию и размеры
            if (info.IsFlexItem && totalFlexGrow > 0)
            {
                float flexWidth = (info.Style.FlexGrow.Value / totalFlexGrow) * remainingWidth;
                info.Element.Size = new Vector2(flexWidth, info.Element.Size.Y);
                info.Element.Position = new Vector2(currentX, availableSpace.Y + marginTop);
                currentX += flexWidth + gap;
            }
            else
            {
                info.Element.Position = new Vector2(currentX, availableSpace.Y + marginTop);
                currentX += info.Element.Size.X + gap;
            }
        }
    }
    
    private static void AlignChildHorizontally(VisualElement child, Style childStyle, 
                                             Rectangle parentSpace, Align alignItems)
    {
        float parentWidth = parentSpace.Width;
        float childWidth = child.Size.X;
        
        // Учитываем alignSelf, если задан
        Align alignSelf = alignItems;
        if (childStyle.AlignSelf.IsSet)
            alignSelf = childStyle.AlignSelf.Value;
        
        switch (alignSelf)
        {
            case Align.Center:
                child.Position = new Vector2(
                    parentSpace.X + (parentWidth - childWidth) / 2,
                    child.Position.Y
                );
                break;
            case Align.FlexEnd:
                child.Position = new Vector2(
                    parentSpace.X + parentWidth - childWidth,
                    child.Position.Y
                );
                break;
            case Align.Stretch:
                if (!childStyle.Width.IsSet)
                {
                    child.Size = new Vector2(parentWidth, child.Size.Y);
                }
                break;
        }
    }
    
    private static void AlignChildVertically(VisualElement child, Style childStyle, 
                                           Rectangle parentSpace, Align alignItems)
    {
        float parentHeight = parentSpace.Height;
        float childHeight = child.Size.Y;
        
        // Учитываем alignSelf, если задан
        Align alignSelf = alignItems;
        if (childStyle.AlignSelf.IsSet)
            alignSelf = childStyle.AlignSelf.Value;
        
        switch (alignSelf)
        {
            case Align.Center:
                child.Position = new Vector2(
                    child.Position.X,
                    parentSpace.Y + (parentHeight - childHeight) / 2
                );
                break;
            case Align.FlexEnd:
                child.Position = new Vector2(
                    child.Position.X,
                    parentSpace.Y + parentHeight - childHeight
                );
                break;
            case Align.Stretch:
                if (!childStyle.Height.IsSet)
                {
                    child.Size = new Vector2(child.Size.X, parentHeight);
                }
                break;
        }
    }
}

public class ChildLayoutInfo
{
    public required VisualElement Element { get; set; }
    public required Style Style { get; set; }
    public bool IsFlexItem { get; set; }
    public Vector2 Size { get; set; }
}