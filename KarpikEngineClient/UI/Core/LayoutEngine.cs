using Raylib_cs;

namespace Karpik.Engine.Client.UIToolkit;

/// <summary>
/// Движок компоновки, отвечающий за расчет геометрии (позиции и размера) UI-элементов.
/// </summary>
public class LayoutEngine
{
    public void Layout(UIElement root, Rectangle viewport)
    {
        LayoutNode(root, viewport);
    }

       private void LayoutNode(UIElement element, Rectangle containingBlock)
    {
        // --- ШАГ 1: Парсинг стилей ---
        var style = element.ComputedStyle;
        bool isBorderBox = style.GetValueOrDefault("box-sizing") == "border-box";
        var widthVal = ParseValue(style.GetValueOrDefault("width", "auto"));
        var heightVal = ParseValue(style.GetValueOrDefault("height", "auto"));
        var margin = ParseEdges(style.GetValueOrDefault("margin", "0"));
        var padding = ParseEdges(style.GetValueOrDefault("padding", "0"));
        var border = ParseEdges(style.GetValueOrDefault("border-width", "0"));

        // --- ШАГ 2: Вычисление ВСЕХ отступов в пикселях (КЛЮЧЕВОЕ ИСПРАВЛЕНИЕ) ---
        // Горизонтальные отступы зависят от ширины родителя
        float marginLeft = margin.Left.ToPx(containingBlock.Width);
        float marginRight = margin.Right.ToPx(containingBlock.Width);
        float paddingLeft = padding.Left.ToPx(containingBlock.Width);
        float paddingRight = padding.Right.ToPx(containingBlock.Width);
        float borderLeft = border.Left.ToPx(containingBlock.Width);
        float borderRight = border.Right.ToPx(containingBlock.Width);
        
        // Вертикальные отступы зависят от высоты родителя
        float marginTop = margin.Top.ToPx(containingBlock.Height);
        float marginBottom = margin.Bottom.ToPx(containingBlock.Height);
        float paddingTop = padding.Top.ToPx(containingBlock.Height);
        float paddingBottom = padding.Bottom.ToPx(containingBlock.Height);
        float borderTop = border.Top.ToPx(containingBlock.Height);
        float borderBottom = border.Bottom.ToPx(containingBlock.Height);

        // --- ШАГ 3: Расчет ширины контента ---
        float horizontalSpacing = marginLeft + borderLeft + paddingLeft + paddingRight + borderRight + marginRight;
        float contentWidth;
        if (widthVal.Unit == Unit.Auto)
        {
            contentWidth = Math.Max(0, containingBlock.Width - horizontalSpacing);
        }
        else
        {
            float totalWidth = widthVal.ToPx(containingBlock.Width);
            contentWidth = isBorderBox 
                ? Math.Max(0, totalWidth - borderLeft - borderRight - paddingLeft - paddingRight) 
                : totalWidth;
        }

        // --- ШАГ 4: Расчет абсолютной позиции для ContentRect элемента ---
        // Абсолютная позиция контента = позиция родительского блока + отступы родителя
        float contentX = containingBlock.X + marginLeft + borderLeft + paddingLeft;
        float contentY = containingBlock.Y + marginTop + borderTop + paddingTop;
        
        // --- ШАГ 5: Рекурсия для дочерних элементов ---
        // Создаем containing block для детей на основе рассчитанной абсолютной позиции и ширины контента родителя
        var childContainingBlock = new Rectangle(contentX, contentY, contentWidth, float.PositiveInfinity);
        float childrenTotalHeight = 0;
        foreach (var child in element.Children)
        {
            // Смещаем блок для следующего ребенка вниз на высоту предыдущего
            var currentChildBlock = new Rectangle(
                childContainingBlock.X,
                childContainingBlock.Y + childrenTotalHeight,
                childContainingBlock.Width,
                childContainingBlock.Height
            );
            
            LayoutNode(child, currentChildBlock);
            
            childrenTotalHeight += child.LayoutBox.MarginRect.Height;
        }

        // --- ШАГ 6: Расчет высоты контента (Bottom-Up) ---
        float contentHeight;
        if (heightVal.Unit == Unit.Auto)
        {
            contentHeight = childrenTotalHeight;
        }
        else
        {
            float totalHeight = heightVal.ToPx(containingBlock.Height);
            contentHeight = isBorderBox
                ? Math.Max(0, totalHeight - borderTop - borderBottom - paddingTop - paddingBottom)
                : totalHeight;
        }
        
        // --- ШАГ 7: Финализация геометрии (построение всех Rect'ов) ---
        // Теперь у нас есть все для построения LayoutBox с абсолютными координатами
        var box = new LayoutBox();
        box.ContentRect = new Rectangle(contentX, contentY, contentWidth, contentHeight);
        box.PaddingRect = box.ContentRect.Inflate(paddingLeft, paddingTop, paddingRight, paddingBottom);
        box.BorderRect = box.PaddingRect.Inflate(borderLeft, borderTop, borderRight, borderBottom);
        box.MarginRect = box.BorderRect.Inflate(marginLeft, marginTop, marginRight, marginBottom);
        element.LayoutBox = box;
    }

    #region Парсеры (без изменений)
    private StyleValue ParseValue(string value)
    {
        // Безопасная обработка пустых или некорректных строк
        if (string.IsNullOrWhiteSpace(value)) return StyleValue.Px(0);
        value = value.Trim();

        // Обработка ключевых слов
        if (value == "auto") return StyleValue.Auto;

        // Обработка значений с единицами измерения
        if (value.EndsWith("px")) return StyleValue.Px(float.Parse(value.Substring(0, value.Length - 2)));
        if (value.EndsWith("%")) return StyleValue.Percent(float.Parse(value.Substring(0, value.Length - 1)));

        // Обработка числовых значений без единиц измерения (по умолчанию считаем их пикселями)
        if (float.TryParse(value, out float num)) return StyleValue.Px(num);

        // Если ничего не подошло, возвращаем безопасное значение по умолчанию
        return StyleValue.Px(0);
    }
    
    private Edges ParseEdges(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return new Edges();
    
        // Разбиваем строку по пробелам и парсим каждую часть в StyleValue
        var parts = value.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(ParseValue)
            .ToArray();
    
        var result = new Edges();

        // Применяем правила сокращенной записи CSS
        switch (parts.Length)
        {
            case 1: // e.g., margin: 10px; (применяется ко всем сторонам)
                result.Top = result.Right = result.Bottom = result.Left = parts[0];
                break;
            case 2: // e.g., margin: 10px 20px; (top/bottom, left/right)
                result.Top = result.Bottom = parts[0];
                result.Right = result.Left = parts[1];
                break;
            case 3: // e.g., margin: 10px 20px 30px; (top, left/right, bottom)
                result.Top = parts[0];
                result.Right = result.Left = parts[1];
                result.Bottom = parts[2];
                break;
            case 4: // e.g., margin: 10px 20px 30px 40px; (top, right, bottom, left)
                result.Top = parts[0];
                result.Right = parts[1];
                result.Bottom = parts[2];
                result.Left = parts[3];
                break;
        }
        return result;
    }
    #endregion
}

#region Классы-расширения (адаптированные под Raylib.Rectangle)

public static class StyleValueExtensions
{
    public static float ToPx(this StyleValue val, float baseValue)
    {
        if (float.IsPositiveInfinity(baseValue) && val.Unit == Unit.Percent) return 0;
        return val.Unit == Unit.Percent ? (val.Value / 100f) * baseValue : val.Value;
    }
}

public static class RectangleExtensions
{
    // "Надувает" прямоугольник на заданные значения с каждой стороны
    public static Rectangle Inflate(this Rectangle rect, float left, float top, float right, float bottom)
    {
        return new Rectangle(
            rect.X - left,
            rect.Y - top,
            rect.Width + left + right,
            rect.Height + top + bottom
        );
    }
    
    public static Rectangle Deflate(this Rectangle rect, float left, float top, float right, float bottom)
    {
        return new Rectangle(
            rect.X + left,
            rect.Y + top,
            rect.Width - left - right,
            rect.Height - top - bottom
        );
    }
}

public static class LayoutBoxExtensions
{
    // Сдвигает все прямоугольники внутри LayoutBox на заданное смещение
    public static void Shift(this LayoutBox box, float dx, float dy)
    {
        box.MarginRect = box.MarginRect.Shifted(dx, dy);
        box.BorderRect = box.BorderRect.Shifted(dx, dy);
        box.PaddingRect = box.PaddingRect.Shifted(dx, dy);
        box.ContentRect = box.ContentRect.Shifted(dx, dy);
    }

    private static Rectangle Shifted(this Rectangle rect, float dx, float dy)
    {
        return new Rectangle(rect.X + dx, rect.Y + dy, rect.Width, rect.Height);
    }
}

#endregion