using System.Text;
using Raylib_cs;
using System.Globalization;

namespace Karpik.Engine.Client.UIToolkit;

public enum LayoutContext { Block, FlexItem }

public class LayoutEngine
{
    private List<UIElement> _absoluteElements;
    private List<UIElement> _fixedElements;

    public void Layout(UIElement root, Rectangle viewport, Font defaultFont)
    {
        _absoluteElements = new List<UIElement>();
        _fixedElements = new List<UIElement>();
        LayoutNode(root, viewport, defaultFont, LayoutContext.Block);

        foreach (var element in _absoluteElements)
        {
            var offsetParent = FindOffsetParent(element);
            var containingBlock = offsetParent?.LayoutBox.PaddingRect ?? viewport;
            LayoutNode(element, containingBlock, defaultFont, LayoutContext.Block);
        }

        foreach (var element in _fixedElements)
        {
            LayoutNode(element, viewport, defaultFont, LayoutContext.Block);
        }
    }

    private void LayoutNode(UIElement element, Rectangle containingBlock, Font defaultFont, LayoutContext context)
    {
        if (element.ComputedStyle.GetValueOrDefault("display") == "none")
        {
            element.LayoutBox = new LayoutBox();
            return;
        }

        var position = element.ComputedStyle.GetValueOrDefault("position", "static");
        if ((position == "absolute" && !_absoluteElements.Contains(element)) ||
            (position == "fixed" && !_fixedElements.Contains(element)))
        {
            if (position == "absolute") _absoluteElements.Add(element);
            if (position == "fixed") _fixedElements.Add(element);
            return;
        }

        var (mLeft, mRight, mTop, mBottom) = ParseAllMargins(element.ComputedStyle, containingBlock);
        var (pLeft, pRight, pTop, pBottom) = ParseAllPaddings(element.ComputedStyle, containingBlock);
        var (bLeft, bRight, bTop, bBottom) = ParseAllBorders(element.ComputedStyle, containingBlock);

        float finalX = containingBlock.X + mLeft;
        float finalY = containingBlock.Y + mTop;

        float availableWidth = containingBlock.Width - mLeft - mRight;
        float availableHeight = containingBlock.Height - mTop - mBottom;

        var widthVal = ParseValue(element.ComputedStyle.GetValueOrDefault("width", "auto"));
        var heightVal = ParseValue(element.ComputedStyle.GetValueOrDefault("height", "auto"));

        // --- НАЧАЛО ИСПРАВЛЕННОГО БЛОКА ---
        float contentWidth;
        float? resolvedWidth = ResolveSize(widthVal, availableWidth, pLeft + pRight + bLeft + bRight);

        if (resolvedWidth.HasValue)
        {
            // Ширина явно задана (px или %)
            contentWidth = resolvedWidth.Value;
        }
        else // Это означает, что width: 'auto'
        {
            if (context == LayoutContext.FlexItem)
            {
                // Для flex-элемента 'auto' означает "естественную" ширину контента.
                // Передаем null в wrapWidth, чтобы текст не переносился и мы измерили его полную длину.
                contentWidth = CalculateNaturalSize(element, defaultFont, null).width;
            }
            else
            {
                // Для обычного блочного элемента 'auto' означает "занять всю доступную ширину".
                contentWidth = availableWidth - pLeft - pRight - bLeft - bRight;
            }
        }
        // --- КОНЕЦ ИСПРАВЛЕННОГО БЛОКА ---

        float contentHeight = ResolveSize(heightVal, availableHeight, pTop + pBottom + bTop + bBottom) ?? 0;

        contentWidth = Math.Max(0, contentWidth);

        if (element.ComputedStyle.GetValueOrDefault("display") == "flex")
        {
            // NOTE: elementBox для flex-контейнера - это его border-box.
            // Ширина уже включает padding и border, поэтому передаем `contentWidth + pLeft + pRight + bLeft + bRight`
            var borderBoxWidth = contentWidth + pLeft + pRight + bLeft + bRight;
            var borderBoxHeight = contentHeight + pTop + pBottom + bTop + bBottom;
            RunFlexLayout(element, new Rectangle(finalX, finalY, borderBoxWidth, borderBoxHeight), defaultFont);
        }
        else
        {
            if (heightVal.Unit == Unit.Auto)
            {
                contentHeight = CalculateNaturalSize(element, defaultFont, contentWidth).height;
            }

            element.LayoutBox = new LayoutBox
            {
                ContentRect = new Rectangle(finalX + bLeft + pLeft, finalY + bTop + pTop, contentWidth, contentHeight)
            };
            RecalculateOuterRects(element, containingBlock);
        }
    }

    private void RunFlexLayout(UIElement element, Rectangle elementBox, Font defaultFont)
    {
        var (pLeft, pRight, pTop, pBottom) = ParseAllPaddings(element.ComputedStyle, elementBox);
        var (bLeft, bRight, bTop, bBottom) = ParseAllBorders(element.ComputedStyle, elementBox);

        // Content area of the flex container
        float contentWidth = elementBox.Width - pLeft - pRight - bLeft - bRight;
        float contentHeight = 0; // Will be determined

        var flexItems = element.Children.Where(c => c.ComputedStyle.GetValueOrDefault("position", "static") == "static")
            .ToList();
        if (!flexItems.Any())
        {
            element.LayoutBox = new LayoutBox
            {
                ContentRect = new Rectangle(elementBox.X + bLeft + pLeft, elementBox.Y + bTop + pTop, contentWidth,
                    contentHeight)
            };
            RecalculateOuterRects(element, new Rectangle(0, 0, elementBox.Width, elementBox.Height));
            return;
        }

        bool isRow = this.isRow(element);

        // --- Шаг 1: Измерение детей ---
        foreach (var item in flexItems)
        {
            LayoutNode(item, new Rectangle(0, 0, isRow ? float.PositiveInfinity : contentWidth, float.PositiveInfinity),
                defaultFont, LayoutContext.FlexItem);
        }

        // --- Шаг 2: Расчёт размеров контейнера (если auto) ---
        if (ParseValue(element.ComputedStyle.GetValueOrDefault("height", "auto")).Unit == Unit.Auto)
        {
            contentHeight = isRow
                ? (flexItems.Any() ? flexItems.Max(i => i.LayoutBox.MarginRect.Height) : 0)
                : flexItems.Sum(i => i.LayoutBox.MarginRect.Height);
        }
        else
        {
            contentHeight = elementBox.Height - pTop - pBottom - bTop - bBottom;
        }

        // --- Шаг 3: Распределение пространства (Flexing) ---
        var baseSizes = new Dictionary<UIElement, float>();
        float totalBaseSize = 0, totalFlexGrow = 0;
        float mainSize = isRow ? contentWidth : contentHeight;

        foreach (var item in flexItems)
        {
            var basis = ParseValue(item.ComputedStyle.GetValueOrDefault("flex-basis", "auto"));
            // Если flex-basis: auto, то используется размер из 'измерительного' прохода
            float baseSize = basis.Unit == Unit.Auto
                ? (isRow ? item.LayoutBox.MarginRect.Width : item.LayoutBox.MarginRect.Height)
                : basis.ToPx(mainSize);
            baseSizes[item] = baseSize;
            totalBaseSize += baseSize;
            totalFlexGrow += ParseFloat(item.ComputedStyle.GetValueOrDefault("flex-grow", "0"));
        }

        float freeSpace = mainSize - totalBaseSize;
        var finalSizes = new Dictionary<UIElement, float>(baseSizes);

        if (freeSpace > 0 && totalFlexGrow > 0)
        {
            foreach (var item in flexItems)
                finalSizes[item] +=
                    (ParseFloat(item.ComputedStyle.GetValueOrDefault("flex-grow", "0")) / totalFlexGrow) * freeSpace;
        }
        else if (freeSpace < 0)
        {
            float totalWeightedShrink = flexItems.Sum(item =>
                ParseFloat(item.ComputedStyle.GetValueOrDefault("flex-shrink", "1")) * baseSizes[item]);
            if (totalWeightedShrink > 0)
            {
                foreach (var item in flexItems)
                    finalSizes[item] -=
                        ((ParseFloat(item.ComputedStyle.GetValueOrDefault("flex-shrink", "1")) * baseSizes[item]) /
                         totalWeightedShrink) * Math.Abs(freeSpace);
            }
        }

        // --- Шаг 4 и 5: Финальная компоновка, выравнивание и рекурсия ---
        element.LayoutBox = new LayoutBox
        {
            ContentRect = new Rectangle(elementBox.X + bLeft + pLeft, elementBox.Y + bTop + pTop, contentWidth,
                contentHeight)
        };
        RecalculateOuterRects(element, new Rectangle(0, 0, elementBox.Width, elementBox.Height));

        var justifyContent = element.ComputedStyle.GetValueOrDefault("justify-content", "flex-start");
        float actualFreeSpace = mainSize - finalSizes.Values.Sum();
        float initialOffset = actualFreeSpace > 0
            ? (justifyContent == "center" ? actualFreeSpace / 2 : (justifyContent == "flex-end" ? actualFreeSpace : 0))
            : 0;
        float spaceBetween = (justifyContent == "space-between" && flexItems.Count > 1 && actualFreeSpace > 0)
            ? actualFreeSpace / (flexItems.Count - 1)
            : 0;

        float currentMainPos = initialOffset;
        foreach (var item in flexItems)
        {
            float finalMainSize = Math.Max(0, finalSizes[item]);

            var alignItems = element.ComputedStyle.GetValueOrDefault("align-items", "stretch");
            var alignSelf = item.ComputedStyle.GetValueOrDefault("align-self", "auto");
            var effectiveAlign = (alignSelf == "auto") ? alignItems : alignSelf;

            float crossSize = isRow ? contentHeight : contentWidth;
            float itemTotalCrossSize;

            if (isRow)
            {
                var (imL, imR, imT, imB) = ParseAllMargins(item.ComputedStyle, element.LayoutBox.ContentRect);
                var (ibL, ibR, ibT, ibB) = ParseAllBorders(item.ComputedStyle, element.LayoutBox.ContentRect);
                var (ipL, ipR, ipT, ipB) = ParseAllPaddings(item.ComputedStyle, element.LayoutBox.ContentRect);
                // Ширина для расчета высоты текста - это финальная ширина ОСНОВНОЙ оси минус горизонтальные отступы/границы/паддинги
                float itemContentWidth = finalMainSize - imL - imR - ibL - ibR - ipL - ipR;
                var naturalHeight = CalculateNaturalSize(item, defaultFont, itemContentWidth).height;
                itemTotalCrossSize = naturalHeight + ipT + ipB + ibT + ibB + imT + imB;
            }
            else
            {
                itemTotalCrossSize = item.LayoutBox.MarginRect.Width;
            }

            if (effectiveAlign == "stretch" &&
                ParseValue(item.ComputedStyle.GetValueOrDefault(isRow ? "height" : "width", "auto")).Unit == Unit.Auto)
            {
                itemTotalCrossSize = crossSize;
            }

            float crossOffset = 0;
            if (effectiveAlign == "center") crossOffset = (crossSize - itemTotalCrossSize) / 2;
            else if (effectiveAlign == "flex-end") crossOffset = crossSize - itemTotalCrossSize;

            float childX = element.LayoutBox.ContentRect.X + (isRow ? currentMainPos : crossOffset);
            float childY = element.LayoutBox.ContentRect.Y + (isRow ? crossOffset : currentMainPos);

            var finalRect = new Rectangle(childX, childY, isRow ? finalMainSize : itemTotalCrossSize,
                isRow ? itemTotalCrossSize : finalMainSize);

            var (imL_final, imR_final, imT_final, imB_final) =
                ParseAllMargins(item.ComputedStyle, element.LayoutBox.ContentRect);
            var (ipL_final, ipR_final, ipT_final, ipB_final) =
                ParseAllPaddings(item.ComputedStyle, element.LayoutBox.ContentRect);
            var (ibL_final, ibR_final, ibT_final, ibB_final) =
                ParseAllBorders(item.ComputedStyle, element.LayoutBox.ContentRect);

            item.LayoutBox = new LayoutBox
            {
                MarginRect = finalRect
            };

            item.LayoutBox.BorderRect = new Rectangle(
                finalRect.X + imL_final,
                finalRect.Y + imT_final,
                Math.Max(0, finalRect.Width - imL_final - imR_final),
                Math.Max(0, finalRect.Height - imT_final - imB_final)
            );

            item.LayoutBox.PaddingRect = new Rectangle(
                item.LayoutBox.BorderRect.X + ibL_final,
                item.LayoutBox.BorderRect.Y + ibT_final,
                Math.Max(0, item.LayoutBox.BorderRect.Width - ibL_final - ibR_final),
                Math.Max(0, item.LayoutBox.BorderRect.Height - ibT_final - ibB_final)
            );

            item.LayoutBox.ContentRect = new Rectangle(
                item.LayoutBox.PaddingRect.X + ipL_final,
                item.LayoutBox.PaddingRect.Y + ipT_final,
                Math.Max(0, item.LayoutBox.PaddingRect.Width - ipL_final - ipR_final),
                Math.Max(0, item.LayoutBox.PaddingRect.Height - ipT_final - ipB_final)
            );

            if (item.Children.Any())
            {
                if (item.ComputedStyle.GetValueOrDefault("display") == "flex")
                {
                    RunFlexLayout(item, item.LayoutBox.BorderRect, defaultFont);
                }
                else
                {
                    CalculateNaturalSize(item, defaultFont, item.LayoutBox.ContentRect.Width);
                }
            }
            else if (!string.IsNullOrEmpty(item.Text))
            {
                CalculateNaturalSize(item, defaultFont, item.LayoutBox.ContentRect.Width);
            }

            currentMainPos += finalMainSize + spaceBetween;
        }
    }

    private (float width, float height) CalculateNaturalSize(UIElement element, Font defaultFont,
        float? wrapWidth = null)
    {
        var style = element.ComputedStyle;

        float textWidth = 0, textHeight = 0;
        if (!string.IsNullOrEmpty(element.Text))
        {
            var fontSize = ParseValue(style.GetValueOrDefault("font-size", "16")).ToPx(0);
            WrapText(element, wrapWidth ?? float.PositiveInfinity, defaultFont, fontSize);
            if (element.WrappedTextLines.Any())
            {
                textWidth = element.WrappedTextLines.Max(line =>
                    Raylib.MeasureTextEx(defaultFont, line, fontSize, 1).X);
                var lineHeight = ParseValue(style.GetValueOrDefault("line-height", "auto"))
                    .ToPx(fontSize, fontSize * 1.2f);
                textHeight = element.WrappedTextLines.Count * lineHeight;
            }
        }

        return (textWidth, textHeight);
    }

    private float? ResolveSize(StyleValue val, float baseValue, float borderAndPadding)
    {
        if (val.Unit == Unit.Auto) return null;
        return val.ToPx(baseValue);
    }

    #region Unchanged Helper Methods

    private bool isRow(UIElement element) =>
        element.ComputedStyle.GetValueOrDefault("flex-direction", "row").StartsWith("row");

    private void RecalculateOuterRects(UIElement element, Rectangle parentContentBox)
    {
        var (marginLeft, marginRight, marginTop, marginBottom) =
            ParseAllMargins(element.ComputedStyle, parentContentBox);
        var (paddingLeft, paddingRight, paddingTop, paddingBottom) =
            ParseAllPaddings(element.ComputedStyle, parentContentBox);
        var (borderLeft, borderRight, borderTop, borderBottom) =
            ParseAllBorders(element.ComputedStyle, parentContentBox);

        element.LayoutBox.PaddingRect =
            element.LayoutBox.ContentRect.Inflate(paddingLeft, paddingTop, paddingRight, paddingBottom);
        element.LayoutBox.BorderRect =
            element.LayoutBox.PaddingRect.Inflate(borderLeft, borderTop, borderRight, borderBottom);
        element.LayoutBox.MarginRect =
            element.LayoutBox.BorderRect.Inflate(marginLeft, marginTop, marginRight, marginBottom);
    }

    private UIElement FindOffsetParent(UIElement element)
    {
        var parent = element.Parent;
        while (parent != null)
        {
            var position = parent.ComputedStyle.GetValueOrDefault("position", "static");
            if (position is "relative" or "absolute" or "fixed") return parent;
            parent = parent.Parent;
        }

        return null;
    }

    private StyleValue ParseValue(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return StyleValue.Px(0);
        value = value.Trim();
        if (value == "auto") return StyleValue.Auto;
        if (value.EndsWith("px"))
        {
            if (float.TryParse(value[..^2], NumberStyles.Any, CultureInfo.InvariantCulture, out float pxVal))
                return StyleValue.Px(pxVal);
        }

        if (value.EndsWith("%"))
        {
            if (float.TryParse(value[..^1], NumberStyles.Any, CultureInfo.InvariantCulture, out float percentVal))
                return StyleValue.Percent(percentVal);
        }

        if (float.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out float numVal))
            return StyleValue.Px(numVal);
        return StyleValue.Px(0);
    }

    private Edges ParseEdges(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return new Edges();
        var parts = value.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).Select(ParseValue).ToArray();
        return parts.Length switch
        {
            1 => new Edges { Top = parts[0], Right = parts[0], Bottom = parts[0], Left = parts[0] },
            2 => new Edges { Top = parts[0], Bottom = parts[0], Right = parts[1], Left = parts[1] },
            3 => new Edges { Top = parts[0], Right = parts[1], Left = parts[1], Bottom = parts[2] },
            4 => new Edges { Top = parts[0], Right = parts[1], Bottom = parts[2], Left = parts[3] },
            _ => new Edges()
        };
    }

    private float ParseFloat(string value, float defaultValue = 0f)
    {
        if (string.IsNullOrWhiteSpace(value)) return defaultValue;
        value = value.Replace("px", "").Trim();
        return float.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out float result)
            ? result
            : defaultValue;
    }

    private (float, float, float, float) ParseAllMargins(Dictionary<string, string> style, Rectangle c) => (
        ParseEdges(style.GetValueOrDefault("margin", "0")).Left.ToPx(c.Width),
        ParseEdges(style.GetValueOrDefault("margin", "0")).Right.ToPx(c.Width),
        ParseEdges(style.GetValueOrDefault("margin", "0")).Top.ToPx(c.Height),
        ParseEdges(style.GetValueOrDefault("margin", "0")).Bottom.ToPx(c.Height));

    private (float, float, float, float) ParseAllPaddings(Dictionary<string, string> style, Rectangle c) => (
        ParseEdges(style.GetValueOrDefault("padding", "0")).Left.ToPx(c.Width),
        ParseEdges(style.GetValueOrDefault("padding", "0")).Right.ToPx(c.Width),
        ParseEdges(style.GetValueOrDefault("padding", "0")).Top.ToPx(c.Height),
        ParseEdges(style.GetValueOrDefault("padding", "0")).Bottom.ToPx(c.Height));

    private (float, float, float, float) ParseAllBorders(Dictionary<string, string> style, Rectangle c) => (
        ParseEdges(style.GetValueOrDefault("border-width", "0")).Left.ToPx(c.Width),
        ParseEdges(style.GetValueOrDefault("border-width", "0")).Right.ToPx(c.Width),
        ParseEdges(style.GetValueOrDefault("border-width", "0")).Top.ToPx(c.Height),
        ParseEdges(style.GetValueOrDefault("border-width", "0")).Bottom.ToPx(c.Height));

    private void WrapText(UIElement e, float maxWidth, Font font, float fontSize)
    {
        e.WrappedTextLines.Clear();
        if (string.IsNullOrEmpty(e.Text)) return;
        if (maxWidth <= 0) maxWidth = float.MaxValue;
        var words = e.Text.Split(' ');
        var line = new StringBuilder();
        foreach (var word in words)
        {
            var testLine = line.Length > 0 ? line + " " + word : word;
            if (Raylib.MeasureTextEx(font, testLine, fontSize, 1).X > maxWidth && line.Length > 0)
            {
                e.WrappedTextLines.Add(line.ToString());
                line.Clear().Append(word);
            }
            else
            {
                if (line.Length > 0) line.Append(' ');
                line.Append(word);
            }
        }

        if (line.Length > 0) e.WrappedTextLines.Add(line.ToString());
    }

    #endregion
}

#region Extension Classes and Structs

public static class StyleValueExtensions
{
    public static float ToPx(this StyleValue val, float baseValue, float defaultValue = 0f) => val.Unit switch
    {
        Unit.Auto => defaultValue, Unit.Percent when float.IsInfinity(baseValue) => defaultValue,
        Unit.Percent => (val.Value / 100f) * baseValue, _ => val.Value
    };
}

public static class RectangleExtensions
{
    public static Rectangle Inflate(this Rectangle rect, float left, float top, float right, float bottom) =>
        new(rect.X - left, rect.Y - top, rect.Width + left + right, rect.Height + top + bottom);
}

#endregion