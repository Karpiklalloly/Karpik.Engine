using Raylib_cs;

namespace Karpik.Engine.Client.UIToolkit;

public enum LayoutType
{
    Block,
    InlineBlock
}

/// <summary>
/// Движок компоновки, отвечающий за расчет геометрии (позиции и размера) UI-элементов.
/// </summary>
public class LayoutEngine
{
    private List<UIElement> _absoluteElements;
    private List<UIElement> _fixedElements;

    public void Layout(UIElement root, Rectangle viewport)
    {
        _absoluteElements = new List<UIElement>();
        _fixedElements = new List<UIElement>();

        // Проход 1: Элементы в потоке
        LayoutNode(root, viewport);

        // Проход 2: Абсолютно позиционированные элементы
        foreach (var element in _absoluteElements)
        {
            var offsetParent = FindOffsetParent(element);
            var containingBlock = offsetParent?.LayoutBox.PaddingRect ?? viewport;
            LayoutNode(element, containingBlock);
        }
        
        // Проход 3: Фиксированно позиционированные элементы
        foreach (var element in _fixedElements)
        {
            LayoutNode(element, viewport);
        }
    }

    private void LayoutNode(UIElement element, Rectangle containingBlock)
    {
        var style = element.ComputedStyle;

        if (style.GetValueOrDefault("display") == "none")
        {
            element.LayoutBox = new LayoutBox { ContentRect = new Rectangle(0, 0, 0, 0) };
            return;
        }

        var position = style.GetValueOrDefault("position", "static");
        if ((position == "absolute" && !_absoluteElements.Contains(element)) ||
            (position == "fixed" && !_fixedElements.Contains(element)))
        {
            // Откладываем обработку элементов, которые не в потоке
            if (position == "absolute") _absoluteElements.Add(element);
            if (position == "fixed") _fixedElements.Add(element);

            // Для элементов вне потока, мы не должны оставлять "дыру" в макете,
            // поэтому их LayoutBox изначально пустой в контексте родителя.
            element.LayoutBox = new LayoutBox
                { MarginRect = new Rectangle(containingBlock.X, containingBlock.Y, 0, 0) };
            return;
        }

        bool isBorderBox = style.GetValueOrDefault("box-sizing") == "border-box";
        var margin = ParseEdges(style.GetValueOrDefault("margin", "0"));
        var padding = ParseEdges(style.GetValueOrDefault("padding", "0"));
        var border = ParseEdges(style.GetValueOrDefault("border-width", "0"));

        float marginLeft = margin.Left.ToPx(containingBlock.Width);
        float marginRight = margin.Right.ToPx(containingBlock.Width);
        float paddingTop = padding.Top.ToPx(containingBlock.Height);
        float paddingBottom = padding.Bottom.ToPx(containingBlock.Height);
        float paddingLeft = padding.Left.ToPx(containingBlock.Width);
        float paddingRight = padding.Right.ToPx(containingBlock.Width);
        float borderLeft = border.Left.ToPx(containingBlock.Width);
        float borderRight = border.Right.ToPx(containingBlock.Width);
        float borderTop = border.Top.ToPx(containingBlock.Height);
        float borderBottom = border.Bottom.ToPx(containingBlock.Height);
        float marginTop = margin.Top.ToPx(containingBlock.Height);
        float marginBottom = margin.Bottom.ToPx(containingBlock.Height);

        var widthVal = ParseValue(style.GetValueOrDefault("width", "auto"));
        var heightVal = ParseValue(style.GetValueOrDefault("height", "auto"));
        var displayType = style.GetValueOrDefault("display", "block") == "inline-block"
            ? LayoutType.InlineBlock
            : LayoutType.Block;

        float top = ParseValue(style.GetValueOrDefault("top", "auto")).ToPx(containingBlock.Height, float.NaN);
        float bottom = ParseValue(style.GetValueOrDefault("bottom", "auto")).ToPx(containingBlock.Height, float.NaN);
        float left = ParseValue(style.GetValueOrDefault("left", "auto")).ToPx(containingBlock.Width, float.NaN);
        float right = ParseValue(style.GetValueOrDefault("right", "auto")).ToPx(containingBlock.Width, float.NaN);

        float contentWidth;
        if (widthVal.Unit == Unit.Auto)
        {
            if (position == "absolute" || position == "fixed")
            {
                if (!float.IsNaN(left) && !float.IsNaN(right))
                    contentWidth = Math.Max(0,
                        containingBlock.Width - (left + right + marginLeft + marginRight + borderLeft + borderRight +
                                                 paddingLeft + paddingRight));
                else
                    contentWidth = 0; // TODO: Shrink-to-fit
            }
            else if (displayType == LayoutType.InlineBlock)
            {
                contentWidth = 0; // TODO: Shrink-to-fit
            }
            else // Block
            {
                contentWidth = Math.Max(0,
                    containingBlock.Width -
                    (marginLeft + marginRight + borderLeft + borderRight + paddingLeft + paddingRight));
            }
        }
        else
        {
            float totalWidth = widthVal.ToPx(containingBlock.Width);
            contentWidth = isBorderBox
                ? Math.Max(0, totalWidth - borderLeft - borderRight - paddingLeft - paddingRight)
                : totalWidth;
        }

        var minWidth = ParseValue(style.GetValueOrDefault("min-width", "0px")).ToPx(containingBlock.Width);
        var maxWidth = ParseValue(style.GetValueOrDefault("max-width", "auto"))
            .ToPx(containingBlock.Width, float.PositiveInfinity);
        contentWidth = Math.Clamp(contentWidth, minWidth, maxWidth);

        float staticX = containingBlock.X + marginLeft;
        float staticY = containingBlock.Y + marginTop;

        float finalX = staticX;
        float finalY = staticY;

        if (position == "relative")
        {
            if (!float.IsNaN(left)) finalX += left;
            else if (!float.IsNaN(right)) finalX -= right;
            if (!float.IsNaN(top)) finalY += top;
            else if (!float.IsNaN(bottom)) finalY -= bottom;
        }
        else if (position == "absolute" || position == "fixed")
        {
            if (!float.IsNaN(left)) finalX = containingBlock.X + left;
            else if (!float.IsNaN(right))
                finalX = containingBlock.X + containingBlock.Width - right - (contentWidth + paddingLeft +
                                                                              paddingRight + borderLeft + borderRight +
                                                                              marginRight);

            if (!float.IsNaN(top)) finalY = containingBlock.Y + top;
        }

        var childContainingBlock = new Rectangle(finalX + borderLeft + paddingLeft, finalY + borderTop + paddingTop,
            contentWidth, float.PositiveInfinity);
        float childrenInFlowHeight = 0;
        float currentLineX = 0;
        float currentLineHeight = 0;

        foreach (var child in element.Children)
        {
            var childDisplay = child.ComputedStyle.GetValueOrDefault("display", "block") == "inline-block"
                ? LayoutType.InlineBlock
                : LayoutType.Block;
            var childPosition = child.ComputedStyle.GetValueOrDefault("position", "static");
            bool isChildInFlow = childPosition == "static" || childPosition == "relative";

            if (isChildInFlow)
            {
                Rectangle currentChildBlock;
                float childWidthWithMargin = ParseValue(child.ComputedStyle.GetValueOrDefault("width", "0"))
                                                 .ToPx(childContainingBlock.Width)
                                             + ParseValue(child.ComputedStyle.GetValueOrDefault("margin-left", "0"))
                                                 .ToPx(childContainingBlock.Width)
                                             + ParseValue(child.ComputedStyle.GetValueOrDefault("margin-right", "0"))
                                                 .ToPx(childContainingBlock.Width);

                if (childDisplay == LayoutType.InlineBlock &&
                    (currentLineX + childWidthWithMargin > childContainingBlock.Width))
                {
                    childrenInFlowHeight += currentLineHeight;
                    currentLineHeight = 0;
                    currentLineX = 0;
                }

                if (childDisplay == LayoutType.InlineBlock)
                {
                    currentChildBlock = new Rectangle(childContainingBlock.X + currentLineX,
                        childContainingBlock.Y + childrenInFlowHeight, childContainingBlock.Width - currentLineX,
                        childContainingBlock.Height);
                }
                else
                {
                    childrenInFlowHeight += currentLineHeight;
                    currentLineHeight = 0;
                    currentLineX = 0;
                    currentChildBlock = new Rectangle(childContainingBlock.X,
                        childContainingBlock.Y + childrenInFlowHeight, childContainingBlock.Width,
                        childContainingBlock.Height);
                }

                LayoutNode(child, currentChildBlock);

                if (childDisplay == LayoutType.InlineBlock)
                {
                    currentLineX += child.LayoutBox.MarginRect.Width;
                    currentLineHeight = Math.Max(currentLineHeight, child.LayoutBox.MarginRect.Height);
                }
                else
                {
                    childrenInFlowHeight += child.LayoutBox.MarginRect.Height;
                }
            }
            else
            {
                // Дети вне потока наследуют containing block родителя
                LayoutNode(child, containingBlock);
            }
        }

        childrenInFlowHeight += currentLineHeight;

        float contentHeight;
        if (heightVal.Unit == Unit.Auto)
        {
            contentHeight = childrenInFlowHeight;
        }
        else
        {
            float totalHeight = heightVal.ToPx(containingBlock.Height);
            contentHeight = isBorderBox
                ? Math.Max(0, totalHeight - borderTop - borderBottom - paddingTop - paddingBottom)
                : totalHeight;
        }

        var minHeight = ParseValue(style.GetValueOrDefault("min-height", "0px")).ToPx(containingBlock.Height);
        var maxHeight = ParseValue(style.GetValueOrDefault("max-height", "auto"))
            .ToPx(containingBlock.Height, float.PositiveInfinity);
        contentHeight = Math.Clamp(contentHeight, minHeight, maxHeight);

        if (position == "absolute" || position == "fixed")
        {
            if (!float.IsNaN(bottom) && float.IsNaN(top))
            {
                finalY = containingBlock.Y + containingBlock.Height - bottom - (contentHeight + paddingTop +
                    paddingBottom + borderTop + borderBottom + marginBottom);
            }
        }

        var box = new LayoutBox();
        box.ContentRect = new Rectangle(finalX + borderLeft + paddingLeft, finalY + borderTop + paddingTop,
            contentWidth, contentHeight);
        box.PaddingRect = box.ContentRect.Inflate(paddingLeft, paddingTop, paddingRight, paddingBottom);
        box.BorderRect = box.PaddingRect.Inflate(borderLeft, borderTop, borderRight, borderBottom);
        box.MarginRect = box.BorderRect.Inflate(marginLeft, marginTop, marginRight, marginBottom);
        element.LayoutBox = box;
    }

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
    
    private UIElement FindOffsetParent(UIElement element)
    {
        var parent = element.Parent;
        while (parent != null)
        {
            var position = parent.ComputedStyle.GetValueOrDefault("position", "static");
            if (position == "relative" || position == "absolute")
            {
                return parent;
            }
            parent = parent.Parent;
        }
        return null; // Если не найден, вернем null (будет позиционироваться от viewport)
    }
    
    private UIElement FindRoot(UIElement element)
    {
        while (element.Parent != null) element = element.Parent;
        return element;
    }
}

#region Классы-расширения (адаптированные под Raylib.Rectangle)

public static class StyleValueExtensions
{
    public static float ToPx(this StyleValue val, float baseValue, float defaultValue = 0f)
    {
        if (val.Unit == Unit.Auto) return defaultValue;
        if (float.IsPositiveInfinity(baseValue) && val.Unit == Unit.Percent) return defaultValue;
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