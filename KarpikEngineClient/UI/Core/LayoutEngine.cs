using System.Numerics;
using Raylib_cs;
using Karpik.Engine.Client.UIToolkit;

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
        
        element.ResolvedStyle.CopyFrom(computedStyle);
        
        // 1. Рассчитываем размеры элемента
        CalculateElementSize(element, computedStyle, availableSpace);
        
        // 2. Рассчитываем позицию для абсолютно и фиксированно позиционированных элементов
        if (computedStyle.Position == Position.Absolute)
        {
            CalculateAbsolutePosition(element, computedStyle, availableSpace);
        }
        else if (computedStyle.Position == Position.Fixed)
        {
            CalculateFixedPosition(element, computedStyle);
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
        
        if (style.Width.HasValue) 
        {
            size.X = style.Width.Value;
        }
        else if (element.Parent == null) 
        {
            size.X = availableSpace.Width;
        }
        else
        {
            // Автоматическое вычисление размера для элементов с содержимым
            size.X = CalculateIntrinsicWidth(element, style);
        }
            
        if (style.Height.HasValue) 
        {
            size.Y = style.Height.Value;
        }
        else if (element.Parent == null) 
        {
            size.Y = availableSpace.Height;
        }
        else
        {
            // Автоматическое вычисление высоты для элементов с содержимым
            size.Y = CalculateIntrinsicHeight(element, style);
        }
        
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
    
    private static void CalculateFixedPosition(VisualElement element, Style style)
    {
        var position = element.Position;
        
        // Fixed позиционирование относительно viewport (экрана)
        if (style.Left.HasValue)
            position.X = style.Left.Value;
        else if (style.Right.HasValue)
            position.X = Raylib.GetScreenWidth() - element.Size.X - style.Right.Value;
            
        if (style.Top.HasValue)
            position.Y = style.Top.Value;
        else if (style.Bottom.HasValue)
            position.Y = Raylib.GetScreenHeight() - element.Size.Y - style.Bottom.Value;
        
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
            case FlexDirection.Column:
                LayoutColumn(visibleChildren, parentStyle, contentArea, false);
                break;
            case FlexDirection.ColumnReverse:
                LayoutColumn(visibleChildren, parentStyle, contentArea, true);
                break;
            case FlexDirection.Row:
                LayoutRow(visibleChildren, parentStyle, contentArea, false);
                break;
            case FlexDirection.RowReverse:
                LayoutRow(visibleChildren, parentStyle, contentArea, true);
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
            else if (childStyle.Position == Position.Fixed)
            {
                // Фиксированно позиционированные элементы позиционируются относительно viewport
                childContentArea = new Rectangle(0, 0, Raylib.GetScreenWidth(), Raylib.GetScreenHeight());
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
    
    private static void LayoutColumn(List<VisualElement> children, Style parentStyle, Rectangle contentArea, bool reverse)
    {
        float totalHeight = 0;
        float totalFlexGrow = 0;
        var childInfos = new List<ChildInfo>();
        
        // Обращаем порядок детей если нужно
        if (reverse)
            children = children.AsEnumerable().Reverse().ToList();
        
        // Собираем информацию о детях
        foreach (var child in children)
        {
            var childStyle = child.ComputeStyle();
            var info = new ChildInfo { Element = child, Style = childStyle };
            
            CalculateElementSize(child, childStyle, contentArea);
            
            // Вычисляем baseline для каждого элемента
            info.Baseline = CalculateBaseline(child, childStyle);
            
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
        
        float spaceBetween = 0;
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
                spaceBetween = childInfos.Count > 1 ? remainingHeight / (childInfos.Count - 1) : 0;
                break;
            case JustifyContent.SpaceAround:
                spaceBetween = remainingHeight / childInfos.Count;
                currentY += spaceBetween / 2;
                break;
            case JustifyContent.SpaceEvenly:
                spaceBetween = remainingHeight / (childInfos.Count + 1);
                currentY += spaceBetween;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(parentStyle), parentStyle.JustifyContent.ToString());
        }
        
        // Для baseline выравнивания в колонке находим максимальный baseline среди элементов в одной строке
        // В вертикальном layout baseline менее актуален, но для совместимости поддерживаем
        float maxBaseline = 0;
        if (parentStyle.AlignItems == AlignItems.Baseline)
        {
            maxBaseline = childInfos.Max(info => info.Baseline + info.Style.Margin.Left);
        }
        
        for (int i = 0; i < childInfos.Count; i++)
        {
            var info = childInfos[i];
            var child = info.Element;
            var childStyle = info.Style;
            
            currentY += childStyle.Margin.Top;
            
            float childX = contentArea.X + childStyle.Margin.Left;
            switch (parentStyle.AlignItems)
            {
                case AlignItems.FlexStart:
                    // Позиция по умолчанию - уже установлена выше
                    break;
                case AlignItems.FlexEnd:
                    childX = contentArea.X + contentArea.Width - child.Size.X - childStyle.Margin.Right;
                    break;
                case AlignItems.Center:
                    childX = contentArea.X + (contentArea.Width - child.Size.X) / 2;
                    break;
                case AlignItems.Stretch:
                    if (!childStyle.Width.HasValue)
                    {
                        child.Size = new Vector2(contentArea.Width - childStyle.Margin.Left - childStyle.Margin.Right, child.Size.Y);
                    }
                    break;
                case AlignItems.Baseline:
                    // В вертикальном layout baseline применяется по горизонтали
                    // Выравниваем по baseline текста внутри элементов
                    childX = contentArea.X + maxBaseline - info.Baseline;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(parentStyle), parentStyle.AlignItems.ToString());
            }
            
            // Устанавливаем позицию
            child.Position = new Vector2(childX, currentY);
            
            // Рассчитываем высоту для flex элементов
            if (info.IsFlexItem && totalFlexGrow > 0)
            {
                float flexHeight = childStyle.FlexGrow / totalFlexGrow * remainingHeight;
                child.Size = new Vector2(child.Size.X, flexHeight);
                currentY += flexHeight;
            }
            else
            {
                currentY += child.Size.Y;
            }
            
            currentY += childStyle.Margin.Bottom;
            
            // Добавляем пространство между элементами для SpaceBetween, SpaceAround, SpaceEvenly
            if (spaceBetween > 0 && i < childInfos.Count - 1)
            {
                currentY += spaceBetween;
            }
        }
    }
    
    private static void LayoutRow(List<VisualElement> children, Style parentStyle, Rectangle contentArea, bool reverse)
    {
        float totalWidth = 0;
        float totalFlexGrow = 0;
        var childInfos = new List<ChildInfo>();
        
        // Обращаем порядок детей если нужно
        if (reverse)
            children = children.AsEnumerable().Reverse().ToList();
        
        // Собираем информацию о детях
        foreach (var child in children)
        {
            var childStyle = child.ComputeStyle();
            var info = new ChildInfo { Element = child, Style = childStyle };
            
            CalculateElementSize(child, childStyle, contentArea);
            
            // Вычисляем baseline для каждого элемента
            info.Baseline = CalculateBaseline(child, childStyle);
            
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

        float spaceBetween = 0;
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
                spaceBetween = childInfos.Count > 1 ? remainingWidth / (childInfos.Count - 1) : 0;
                break;
            case JustifyContent.SpaceAround:
                spaceBetween = remainingWidth / childInfos.Count;
                currentX += spaceBetween / 2;
                break;
            case JustifyContent.SpaceEvenly:
                spaceBetween = remainingWidth / (childInfos.Count + 1);
                currentX += spaceBetween;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(parentStyle), parentStyle.JustifyContent.ToString());
        }
        
        // Для baseline выравнивания находим максимальный baseline
        float maxBaseline = 0;
        if (parentStyle.AlignItems == AlignItems.Baseline)
        {
            maxBaseline = childInfos.Max(info => info.Baseline + info.Style.Margin.Top);
        }
        
        for (int i = 0; i < childInfos.Count; i++)
        {
            var info = childInfos[i];
            var child = info.Element;
            var childStyle = info.Style;
            
            currentX += childStyle.Margin.Left;
            
            float childY = contentArea.Y + childStyle.Margin.Top;
            switch (parentStyle.AlignItems)
            {
                case AlignItems.FlexStart:
                    // Позиция по умолчанию - уже установлена выше
                    break;
                case AlignItems.FlexEnd:
                    childY = contentArea.Y + contentArea.Height - child.Size.Y - childStyle.Margin.Bottom;
                    break;
                case AlignItems.Center:
                    childY = contentArea.Y + (contentArea.Height - child.Size.Y) / 2;
                    break;
                case AlignItems.Stretch:
                    if (!childStyle.Height.HasValue)
                    {
                        child.Size = new Vector2(child.Size.X, contentArea.Height - childStyle.Margin.Top - childStyle.Margin.Bottom);
                    }
                    break;
                case AlignItems.Baseline:
                    // Выравниваем по baseline - позиционируем так чтобы baseline всех элементов совпадал
                    childY = contentArea.Y + maxBaseline - info.Baseline;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(parentStyle), parentStyle.AlignItems.ToString());
            }
            
            child.Position = new Vector2(currentX, childY);
            
            // Рассчитываем ширину для flex элементов
            if (info.IsFlexItem && totalFlexGrow > 0)
            {
                float flexWidth = childStyle.FlexGrow / totalFlexGrow * remainingWidth;
                child.Size = new Vector2(flexWidth, child.Size.Y);
                currentX += flexWidth;
            }
            else
            {
                currentX += child.Size.X;
            }
            
            currentX += childStyle.Margin.Right;
            
            // Добавляем пространство между элементами для SpaceBetween, SpaceAround, SpaceEvenly
            if (spaceBetween > 0 && i < childInfos.Count - 1)
            {
                currentX += spaceBetween;
            }
        }
    }
    
    private class ChildInfo
    {
        public VisualElement Element { get; set; } = null!;
        public Style Style { get; set; } = null!;
        public bool IsFlexItem { get; set; }
        public float Baseline { get; set; }
    }
    
    private static Rectangle GetRootAvailableSpace(VisualElement element)
    {
        // Поднимаемся до корневого элемента
        var root = element;
        while (root.Parent != null)
            root = root.Parent;
            
        return new Rectangle(0, 0, root.Size.X, root.Size.Y);
    }
    
    private static float CalculateBaseline(VisualElement element, Style style)
    {
        // Baseline для текстовых элементов - это позиция где должна располагаться базовая линия текста
        // Для простоты используем 80% от высоты элемента (примерно где располагается baseline текста)
        // В более сложной реализации можно было бы учитывать реальные метрики шрифта
        
        if (element.Children.Count == 0)
        {
            // Листовой элемент - вычисляем baseline на основе размера шрифта
            float fontSize = style.FontSize;
            float elementHeight = element.Size.Y;
            
            // Baseline обычно находится на расстоянии примерно 0.8 от высоты шрифта от верха
            float baselineFromTop = Math.Max(fontSize * 0.8f, elementHeight * 0.8f);
            return Math.Min(baselineFromTop, elementHeight);
        }
        else
        {
            // Для контейнеров используем baseline первого текстового дочернего элемента
            foreach (var child in element.Children)
            {
                if (child.Visible)
                {
                    var childStyle = child.ComputeStyle();
                    return CalculateBaseline(child, childStyle);
                }
            }
            
            // Если нет дочерних элементов, используем 80% высоты
            return element.Size.Y * 0.8f;
        }
    }
    
    private static float CalculateIntrinsicWidth(VisualElement element, Style style)
    {
        // Если у элемента уже есть размер, используем его
        if (element.Size.X > 0)
        {
            return element.Size.X;
        }
        
        // Универсальный расчет ширины на основе текстового содержимого
        float calculatedWidth = 0;
        var padding = style.Padding.Left + style.Padding.Right;
        
        // Проверяем текстовое содержимое элемента (если он реализует ITextProvider)
        if (element is ITextProvider textProvider)
        {
            // Проверяем основной текст элемента
            var displayText = textProvider.GetDisplayText();
            if (!string.IsNullOrEmpty(displayText))
            {
                var textWidth = Raylib.MeasureText(displayText, style.FontSize);
                calculatedWidth = Math.Max(calculatedWidth, textWidth);
            }
            
            // Проверяем placeholder текст
            var placeholderText = textProvider.GetPlaceholderText();
            if (!string.IsNullOrEmpty(placeholderText))
            {
                var placeholderWidth = Raylib.MeasureText(placeholderText, style.FontSize);
                calculatedWidth = Math.Max(calculatedWidth, placeholderWidth);
            }
            
            // Проверяем все опции (для dropdown и подобных элементов)
            var textOptions = textProvider.GetTextOptions();
            if (textOptions != null)
            {
                foreach (var option in textOptions)
                {
                    if (!string.IsNullOrEmpty(option))
                    {
                        var optionWidth = Raylib.MeasureText(option, style.FontSize);
                        calculatedWidth = Math.Max(calculatedWidth, optionWidth);
                    }
                }
                
                // Добавляем дополнительное место для UI элементов (стрелки, иконки и т.д.)
                calculatedWidth += 30;
            }
        }
        
        // Добавляем padding
        calculatedWidth += padding;
        
        // Применяем минимальную ширину из стилей
        var minimumWidth = style.MinWidth ?? 0;
        calculatedWidth = Math.Max(calculatedWidth, minimumWidth);
        
        // Если есть рассчитанная ширина, используем её
        if (calculatedWidth > 0)
        {
            return calculatedWidth;
        }
        
        // Для контейнеров используем разумные значения по умолчанию
        if (element.Children.Count > 0)
        {
            // Если есть MinWidth, используем его
            if (style.MinWidth.HasValue)
            {
                return style.MinWidth.Value;
            }
            
            // Для разных типов контейнеров используем разные значения по умолчанию
            if (element.Name == "ContentArea" || element.Name == "Modal")
            {
                return 400; // Разумный размер для модальных окон
            }
            
            if (style.FlexDirection == FlexDirection.Row)
            {
                return 300; // Для горизонтальных контейнеров
            }
            else
            {
                return 250; // Для вертикальных контейнеров
            }
        }
        
        // По умолчанию возвращаем минимальную ширину или 0
        return style.MinWidth ?? 0;
    }
    
    private static float CalculateIntrinsicHeight(VisualElement element, Style style)
    {
        // Если у элемента уже есть размер, используем его
        if (element.Size.Y > 0)
        {
            return element.Size.Y;
        }
        
        // Универсальный расчет высоты для элементов с текстом
        var padding = style.Padding.Top + style.Padding.Bottom;
        var calculatedHeight = style.FontSize + padding;
        
        // Добавляем дополнительное место для UI элементов (если есть текст)
        if (element is ITextProvider textProvider)
        {
            var displayText = textProvider.GetDisplayText();
            if (!string.IsNullOrEmpty(displayText))
            {
                calculatedHeight += 10; // Дополнительное место для интерактивных элементов
            }
        }
        
        // Применяем минимальную высоту из стилей
        var minimumHeight = style.MinHeight ?? 0;
        calculatedHeight = Math.Max(calculatedHeight, minimumHeight);
        
        // Если есть рассчитанная высота, используем её
        if (calculatedHeight > 0)
        {
            return calculatedHeight;
        }
        
        // Для контейнеров используем разумные значения по умолчанию
        if (element.Children.Count > 0)
        {
            // Если есть MinHeight, используем его
            if (style.MinHeight.HasValue)
            {
                return style.MinHeight.Value;
            }
            
            // Для разных типов контейнеров используем разные значения по умолчанию
            if (element.Name == "ContentArea" || element.Name == "Modal")
            {
                return 300; // Разумный размер для модальных окон
            }
            
            if (style.FlexDirection == FlexDirection.Column)
            {
                return 200; // Для вертикальных контейнеров
            }
            else
            {
                return 150; // Для горизонтальных контейнеров
            }
        }
        
        // По умолчанию возвращаем минимальную высоту или 0
        return style.MinHeight ?? 0;
    }}
