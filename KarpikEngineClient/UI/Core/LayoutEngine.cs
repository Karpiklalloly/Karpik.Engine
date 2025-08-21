using System.Text;
using Raylib_cs;
using System.Globalization;
using System.Collections.Generic;
using System.Linq;
using System;

using s = Karpik.Engine.Client.UIToolkit.StyleSheet;

namespace Karpik.Engine.Client.UIToolkit;

public enum LayoutContext { Block, FlexItem }

public class LayoutEngine
{
    private readonly List<UIElement> _absoluteElements = [];
    private readonly List<UIElement> _fixedElements = [];
    private Rectangle _viewport;

    public void Layout(UIElement root, Rectangle viewport, Font defaultFont)
    {
        _absoluteElements.Clear();
        _fixedElements.Clear();

        _viewport = viewport;

        FindFixedAndAbsolute(root);

        Calculate(root, viewport, defaultFont);

        foreach (var element in _absoluteElements)
        {
            var containingBlock = FindOffsetParent(element) ?? root;
            var availableSpace = containingBlock.LayoutBox.PaddingRect;
            Calculate(element, availableSpace, defaultFont);
        }

        foreach (var element in _fixedElements)
        {
            Calculate(element, _viewport, defaultFont);
        }
    }

    private void FindFixedAndAbsolute(UIElement parent)
    {
        foreach (var child in parent.Children)
        {
            switch (child.GetPosition())
            {
                case s.position_fixed:
                    _fixedElements.Add(child);
                    break;
                case s.position_absolute:
                    _absoluteElements.Add(child);
                    break;
                default:
                    FindFixedAndAbsolute(child);
                    break;
            }
        }
    }

    private void Calculate(UIElement parent, Rectangle availableSpace, Font font)
    {
        if (parent.ComputedStyle.GetValueOrDefault("display") == "none")
        {
            parent.LayoutBox = new LayoutBox();
            return;
        }

        var style = parent.ComputedStyle;
        var position = parent.GetPosition();

        Rectangle sizingBlock;
        if (position == s.position_fixed) sizingBlock = _viewport;
        else if (parent.Parent != null) sizingBlock = parent.Parent.LayoutBox.ContentRect;
        else sizingBlock = availableSpace;

        var marginLeft = ParseValue(style.GetValueOrDefault(s.margin_left, "0")).ToPx(sizingBlock.Width);
        var marginRight = ParseValue(style.GetValueOrDefault(s.margin_right, "0")).ToPx(sizingBlock.Width);
        var borderLeft = ParseValue(style.GetValueOrDefault(s.border_left_width, "0")).ToPx(0);
        var borderRight = ParseValue(style.GetValueOrDefault(s.border_right_width, "0")).ToPx(0);
        var paddingLeft = ParseValue(style.GetValueOrDefault(s.padding_left, "0")).ToPx(0);
        var paddingRight = ParseValue(style.GetValueOrDefault(s.padding_right, "0")).ToPx(0);

        var widthVal = ParseValue(style.GetValueOrDefault(s.width, s.auto));
        float borderBoxWidth;

        if (widthVal.Unit != Unit.Auto)
        {
            borderBoxWidth = widthVal.ToPx(sizingBlock.Width);
        }
        else
        {
            if (float.IsInfinity(availableSpace.Width))
            {
                // Здесь CalculateNaturalSize вызывается с wrapWidth и работает корректно
                var naturalSize = CalculateNaturalSize(parent, font, float.PositiveInfinity);
                borderBoxWidth = naturalSize.width + paddingLeft + paddingRight + borderLeft + borderRight;
            }
            else
            {
                borderBoxWidth = availableSpace.Width - marginLeft - marginRight;
            }
        }

        float contentWidth = Math.Max(0, borderBoxWidth - paddingLeft - paddingRight - borderLeft - borderRight);

        var fontSize = ParseValue(style.GetValueOrDefault(s.font_size, "16")).ToPx(0);
        // Это финальный перенос по уже известной ширине
        WrapText(parent, contentWidth, font, fontSize);

        var marginTop = ParseValue(style.GetValueOrDefault(s.margin_top, "0")).ToPx(sizingBlock.Height);
        var marginBottom = ParseValue(style.GetValueOrDefault(s.margin_bottom, "0")).ToPx(sizingBlock.Height);
        var borderTop = ParseValue(style.GetValueOrDefault(s.border_top_width, "0")).ToPx(0);
        var borderBottom = ParseValue(style.GetValueOrDefault(s.border_bottom_width, "0")).ToPx(0);
        var paddingTop = ParseValue(style.GetValueOrDefault(s.padding_top, "0")).ToPx(0);
        var paddingBottom = ParseValue(style.GetValueOrDefault(s.padding_bottom, "0")).ToPx(0);

        float finalX, finalY;
        if (position is "static" or "relative")
        {
            finalX = availableSpace.X + marginLeft;
            finalY = availableSpace.Y + marginTop;
        }
        else
        {
            var leftVal = ParseValue(style.GetValueOrDefault(s.left, s.auto));
            var rightVal = ParseValue(style.GetValueOrDefault(s.right, s.auto));
            var topVal = ParseValue(style.GetValueOrDefault(s.top, s.auto));
            var bottomVal = ParseValue(style.GetValueOrDefault(s.bottom, s.auto));

            if (leftVal.Unit != Unit.Auto)
            {
                finalX = availableSpace.X + leftVal.ToPx(sizingBlock.Width);
            }
            else if (rightVal.Unit != Unit.Auto)
            {
                finalX = availableSpace.X + sizingBlock.Width - rightVal.ToPx(sizingBlock.Width) - borderBoxWidth -
                         marginRight;
            }
            else
            {
                finalX = availableSpace.X + marginLeft;
            }

            if (topVal.Unit != Unit.Auto)
            {
                finalY = availableSpace.Y + topVal.ToPx(sizingBlock.Height);
            }
            else if (bottomVal.Unit != Unit.Auto)
            {
                finalY = availableSpace.Y + marginTop;
            }
            else
            {
                finalY = availableSpace.Y + marginTop;
            }
        }

        // =================================================================
        // ШАГ 4: РАСЧЕТ ВЫСОТЫ
        // =================================================================
        float finalContentHeight = 0;
        var heightVal = ParseValue(style.GetValueOrDefault(s.height, s.auto));
        bool heightDependsOnContent = true;

        if (heightVal.Unit == Unit.Px)
        {
            finalContentHeight = heightVal.ToPx(sizingBlock.Height) - paddingTop - paddingBottom - borderTop -
                                 borderBottom;
            heightDependsOnContent = false;
        }
        else if (heightVal.Unit == Unit.Percent && !float.IsInfinity(sizingBlock.Height))
        {
            finalContentHeight = heightVal.ToPx(sizingBlock.Height) - paddingTop - paddingBottom - borderTop -
                                 borderBottom;
            heightDependsOnContent = false;
        }
        else
        {
            bool isChildOfFlexRow = parent.Parent != null && parent.Parent.GetDisplay() == s.display_flex &&
                                    parent.Parent.IsRow();
            if (isChildOfFlexRow)
            {
                var alignItems = parent.Parent.ComputedStyle.GetValueOrDefault("align-items", "stretch");
                var alignSelf = style.GetValueOrDefault(s.align_self, alignItems);

                if (alignSelf == s.align_self_stretch && heightVal.Unit == Unit.Auto &&
                    !float.IsInfinity(sizingBlock.Height))
                {
                    finalContentHeight = sizingBlock.Height - marginTop - marginBottom - paddingTop - paddingBottom -
                                         borderTop - borderBottom;
                    heightDependsOnContent = false;
                }
            }
        }

        if (heightDependsOnContent)
        {
            // Теперь CalculateNaturalSize вызывается БЕЗ wrapWidth. Он просто измерит
            // WrappedTextLines, которые мы заполнили выше. Поскольку WrapText был вызван,
            // WrappedTextLines НЕ пуст, и высота будет правильной.
            float naturalTextHeight = CalculateNaturalSize(parent, font).height;
            float childrenConsumedHeight = LayoutChildren(parent, font); // Предварительный замер детей
            finalContentHeight = Math.Max(naturalTextHeight, childrenConsumedHeight);
        }

        finalContentHeight = Math.Max(0, finalContentHeight);

        parent.LayoutBox = new LayoutBox
        {
            ContentRect = new Rectangle(
                finalX + borderLeft + paddingLeft,
                finalY + borderTop + paddingTop,
                contentWidth,
                finalContentHeight
            )
        };
        RecalculateOuterRects(parent, marginLeft, marginRight, marginTop, marginBottom, paddingLeft, paddingRight,
            paddingTop, paddingBottom, borderLeft, borderRight, borderTop, borderBottom);

        LayoutChildren(parent, font); // Финальная компоновка детей

        var bottomProp = ParseValue(style.GetValueOrDefault(s.bottom, s.auto));
        var topProp = ParseValue(style.GetValueOrDefault(s.top, s.auto));
        if (position is s.position_absolute or s.position_fixed && topProp.Unit == Unit.Auto &&
            bottomProp.Unit != Unit.Auto)
        {
            float newY = availableSpace.Y + sizingBlock.Height - bottomProp.ToPx(sizingBlock.Height) -
                         parent.LayoutBox.MarginRect.Height;
            parent.LayoutBox.SetY(newY);
        }
    }

    private float LayoutChildren(UIElement parent, Font font)
    {
        return parent.GetDisplay() switch
        {
            s.display_block => CalculateBlock(parent, font),
            s.display_flex => CalculateFlex(parent, font),
            s.display_inline_block => CalculateInlineBlock(parent, font),
            _ => 0
        };
    }

    private float CalculateInlineBlock(UIElement parent, Font font)
    {
        float currentX = 0;
        float currentY = 0;
        float lineHeight = 0;

        foreach (var child in parent.Children)
        {
            if (_absoluteElements.Contains(child) || _fixedElements.Contains(child))
            {
                continue;
            }

            var preLayoutSpace = new Rectangle(0, 0, float.PositiveInfinity, float.PositiveInfinity);
            Calculate(child, preLayoutSpace, font);
            float childMarginWidth = child.LayoutBox.MarginRect.Width;

            if (currentX > 0 && (currentX + childMarginWidth > parent.LayoutBox.ContentRect.Width))
            {
                currentY += lineHeight;
                currentX = 0;
                lineHeight = 0;
            }

            var finalAvailableSpace = new Rectangle(
                parent.LayoutBox.ContentRect.X + currentX,
                parent.LayoutBox.ContentRect.Y + currentY,
                parent.LayoutBox.ContentRect.Width - currentX,
                float.PositiveInfinity
            );
            Calculate(child, finalAvailableSpace, font);

            currentX += child.LayoutBox.MarginRect.Width;
            lineHeight = Math.Max(lineHeight, child.LayoutBox.MarginRect.Height);
        }

        return currentY + lineHeight;
    }

    private float CalculateBlock(UIElement parent, Font font)
    {
        float currentY = 0;
        foreach (var child in parent.Children)
        {
            if (_absoluteElements.Contains(child) || _fixedElements.Contains(child)) continue;

            var availableSpaceForChild = new Rectangle(
                parent.LayoutBox.ContentRect.X,
                parent.LayoutBox.ContentRect.Y + currentY,
                parent.LayoutBox.ContentRect.Width,
                float.PositiveInfinity
            );
            Calculate(child, availableSpaceForChild, font);

            // ИСПРАВЛЕНИЕ: Используем MarginRect.Height для корректного смещения
            currentY += child.LayoutBox.MarginRect.Height;
        }

        return currentY;
    }

    private float CalculateFlex(UIElement parent, Font font)
    {
        var flexItems = parent.Children.Where(c => !_absoluteElements.Contains(c)
                                                   && !_fixedElements.Contains(c)
                                                   && c.GetDisplay() != s.display_none);
        if (!flexItems.Any())
        {
            return 0;
        }

        var style = parent.ComputedStyle;
        var contentBox = parent.LayoutBox.ContentRect;
        bool isRow = parent.IsRow();
        float mainSize = isRow ? contentBox.Width : contentBox.Height;
        if (float.IsInfinity(mainSize)) mainSize = 0;

        float totalFlexBasis = 0;
        var itemData = flexItems.Select(item =>
        {
            var basisVal = ParseValue(item.ComputedStyle.GetValueOrDefault(s.flex_basis, s.auto));
            float flexBasis;

            if (basisVal.Unit == Unit.Auto)
            {
                var availableForSizing = new Rectangle(0, 0,
                    isRow ? float.PositiveInfinity : contentBox.Width,
                    isRow ? contentBox.Height : float.PositiveInfinity
                );
                Calculate(item, availableForSizing, font);
                flexBasis = isRow ? item.LayoutBox.MarginRect.Width : item.LayoutBox.MarginRect.Height;
            }
            else
            {
                flexBasis = basisVal.ToPx(mainSize);
            }

            totalFlexBasis += flexBasis;
            return new { Element = item, Basis = flexBasis };
        }).ToList(); // ToList, чтобы избежать повторного вычисления

        // if (ParseValue(parent.ComputedStyle.GetValueOrDefault(isRow ? s.width : s.height, s.auto)).Unit == Unit.Auto)
        // {
        //     mainSize = totalFlexBasis;
        // }

        float freeSpace = mainSize - totalFlexBasis;
        var finalSizes = new Dictionary<UIElement, float>();

        if (freeSpace > 0) // GROW
        {
            float totalGrow =
                itemData.Sum(d => ParseFloat(d.Element.ComputedStyle.GetValueOrDefault(s.flex_grow, "0")));
            foreach (var item in itemData)
            {
                float growFactor = ParseFloat(item.Element.ComputedStyle.GetValueOrDefault(s.flex_grow, "0"));
                float addedSpace = (totalGrow > 0) ? (growFactor / totalGrow) * freeSpace : 0;
                finalSizes[item.Element] = item.Basis + addedSpace;
            }
        }
        else // SHRINK
        {
            float totalWeightedShrink = itemData.Sum(d =>
                ParseFloat(d.Element.ComputedStyle.GetValueOrDefault(s.flex_shrink, "1")) * d.Basis);
            foreach (var item in itemData)
            {
                float shrinkFactor = ParseFloat(item.Element.ComputedStyle.GetValueOrDefault(s.flex_shrink, "1"));
                float removedSpace = (totalWeightedShrink > 0)
                    ? ((shrinkFactor * item.Basis) / totalWeightedShrink) * freeSpace
                    : 0;
                finalSizes[item.Element] = item.Basis + removedSpace;
            }
        }

        var justifyContent = parent.GetJustifyContent();
        float mainAxisOffset = 0, spacing = 0;
        float finalFreeSpace = mainSize - finalSizes.Values.Sum();

        if (finalFreeSpace > 0)
        {
            var count = flexItems.Count();
            switch (justifyContent)
            {
                case s.justify_content_flex_start: break;
                case s.justify_content_flex_end: mainAxisOffset = finalFreeSpace; break;
                case s.justify_content_center: mainAxisOffset = finalFreeSpace / 2; break;
                case s.justify_content_space_between:
                    if (count > 1) spacing = finalFreeSpace / (count - 1);
                    break;
                case s.justify_content_space_around:
                    spacing = finalFreeSpace / count;
                    mainAxisOffset = spacing / 2;
                    break;
            }
        }

        float maxCrossSize = 0;
        float currentMainPos = mainAxisOffset;

        foreach (var item in flexItems)
        {
            float finalMainSize = Math.Max(0, finalSizes[item]);
            float crossSize = isRow ? contentBox.Height : contentBox.Width;

            var alignItems = style.GetValueOrDefault("align-items", "stretch");
            var alignSelf = item.ComputedStyle.GetValueOrDefault(s.align_self, alignItems);

            Rectangle availableSpaceForChild;
            bool isStretched = alignSelf == s.align_self_stretch &&
                               (isRow
                                   ? ParseValue(item.ComputedStyle.GetValueOrDefault(s.height, s.auto)).Unit ==
                                     Unit.Auto
                                   : ParseValue(item.ComputedStyle.GetValueOrDefault(s.width, s.auto)).Unit ==
                                     Unit.Auto);

            if (isStretched && !float.IsInfinity(crossSize))
            {
                availableSpaceForChild = new Rectangle(
                    isRow ? contentBox.X + currentMainPos : contentBox.X,
                    isRow ? contentBox.Y : contentBox.Y + currentMainPos,
                    isRow ? finalMainSize : crossSize,
                    isRow ? crossSize : finalMainSize);
            }
            else
            {
                availableSpaceForChild = new Rectangle(
                    isRow ? contentBox.X + currentMainPos : contentBox.X,
                    isRow ? contentBox.Y : contentBox.Y + currentMainPos,
                    isRow ? finalMainSize : contentBox.Width,
                    isRow ? float.PositiveInfinity : finalMainSize);
            }

            Calculate(item, availableSpaceForChild, font);

            float itemCrossSize = isRow ? item.LayoutBox.MarginRect.Height : item.LayoutBox.MarginRect.Width;
            maxCrossSize = Math.Max(maxCrossSize, itemCrossSize);

            var itemMargin = ParseAllMargins(item.ComputedStyle, parent.LayoutBox.ContentRect);

            if (alignSelf == s.align_self_flex_end && !float.IsInfinity(crossSize))
            {
                if (isRow) item.LayoutBox.SetY(contentBox.Y + crossSize - itemCrossSize - itemMargin.Bottom);
                else item.LayoutBox.SetX(contentBox.X + crossSize - itemCrossSize - itemMargin.Right);
            }
            else if (alignSelf == s.align_self_center && !float.IsInfinity(crossSize))
            {
                if (isRow)
                    item.LayoutBox.SetY(contentBox.Y + (crossSize - itemCrossSize) / 2 + itemMargin.Top -
                                        itemMargin.Bottom);
                else
                    item.LayoutBox.SetX(contentBox.X + (crossSize - itemCrossSize) / 2 + itemMargin.Left -
                                        itemMargin.Right);
            }

            currentMainPos += finalMainSize + spacing;
        }

        return isRow ? maxCrossSize : mainSize;
    }

    private (float width, float height) CalculateNaturalSize(UIElement element, Font defaultFont,
        float? wrapWidth = null)
    {
        var style = element.ComputedStyle;
        var fontSize = ParseValue(style.GetValueOrDefault(s.font_size, "16")).ToPx(0);

        // Если context для замера (wrapWidth предоставлен), мы ОБЯЗАТЕЛЬНО выполняем перенос.
        // Если context для измерения высоты (wrapWidth is null), мы работаем с текстом,
        // который уже был перенесен в Calculate().
        if (wrapWidth.HasValue)
        {
            WrapText(element, wrapWidth.Value, defaultFont, fontSize);
        }

        float textWidth = 0, textHeight = 0;
        if (element.WrappedTextLines.Any())
        {
            textWidth = element.WrappedTextLines.Max(line =>
                Raylib.MeasureTextEx(defaultFont, line, fontSize, 1).X);
            var lineHeight = ParseValue(style.GetValueOrDefault(s.line_height, "auto"))
                .ToPx(fontSize, fontSize * 1.2f);
            textHeight = element.WrappedTextLines.Count * lineHeight;
        }

        var flowChildren = element.Children
            .Where(c => c.GetPosition() is "static" or "relative" && c.GetDisplay() != "none");

        if (!element.WrappedTextLines.Any() && flowChildren.Any())
        {
            float childrenHeight = 0;
            float maxChildWidth = 0;

            foreach (var child in flowChildren)
            {
                // Рекурсивно замеряем детей
                var childSize = CalculateNaturalSize(child, defaultFont, float.PositiveInfinity);

                if (childSize.width > maxChildWidth)
                {
                    maxChildWidth = childSize.width;
                }

                childrenHeight += childSize.height;
            }

            textWidth = maxChildWidth;
            textHeight = childrenHeight;
        }

        return (textWidth, textHeight);
    }

    private void RecalculateOuterRects(UIElement element,
        float mLeft, float mRight, float mTop, float mBottom,
        float pLeft, float pRight, float pTop, float pBottom,
        float bLeft, float bRight, float bTop, float bBottom)
    {
        var contentRect = element.LayoutBox.ContentRect;

        element.LayoutBox.PaddingRect = new Rectangle(
            contentRect.X - pLeft,
            contentRect.Y - pTop,
            contentRect.Width + pLeft + pRight,
            contentRect.Height + pTop + pBottom
        );

        element.LayoutBox.BorderRect = new Rectangle(
            element.LayoutBox.PaddingRect.X - bLeft,
            element.LayoutBox.PaddingRect.Y - bTop,
            element.LayoutBox.PaddingRect.Width + bLeft + bRight,
            element.LayoutBox.PaddingRect.Height + bTop + bBottom
        );

        element.LayoutBox.MarginRect = new Rectangle(
            element.LayoutBox.BorderRect.X - mLeft,
            element.LayoutBox.BorderRect.Y - mTop,
            element.LayoutBox.BorderRect.Width + mLeft + mRight,
            element.LayoutBox.BorderRect.Height + mTop + mBottom
        );
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

    private static float ParseFloat(string value, float defaultValue = 0f)
    {
        if (string.IsNullOrWhiteSpace(value)) return defaultValue;
        value = value.Replace("px", "").Trim();
        return float.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out float result)
            ? result
            : defaultValue;
    }

    private (float Left, float Right, float Top, float Bottom) ParseAllMargins(Dictionary<string, string> style,
        Rectangle c) => (
        ParseEdges(style.GetValueOrDefault("margin", "0")).Left.ToPx(c.Width),
        ParseEdges(style.GetValueOrDefault("margin", "0")).Right.ToPx(c.Width),
        ParseEdges(style.GetValueOrDefault("margin", "0")).Top.ToPx(c.Height),
        ParseEdges(style.GetValueOrDefault("margin", "0")).Bottom.ToPx(c.Height));

    private void WrapText(UIElement e, float maxWidth, Font font, float fontSize)
    {
        e.WrappedTextLines.Clear();
        if (string.IsNullOrEmpty(e.Text)) return;
        if (maxWidth <= 0) return;
        if (!float.IsInfinity(maxWidth))
        {
            maxWidth = MathF.Round(maxWidth, 6);
        }

        var words = e.Text.Split(' ');
        var line = new StringBuilder();
        foreach (var word in words)
        {
            var testLine = line.Length > 0 ? line + " " + word : word;
            var m = Raylib.MeasureTextEx(font, testLine, fontSize, 1);
            if (m.X > maxWidth + 0.0001f && line.Length > 0)
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
}

public static class StyleValueExtensions
{
    public static float ToPx(this StyleValue val, float baseValue, float defaultValue = 0f) => val.Unit switch
    {
        Unit.Auto => defaultValue,
        Unit.Percent when float.IsInfinity(baseValue) => defaultValue,
        Unit.Percent => (val.Value / 100f) * baseValue, _ => val.Value
    };
}

public static class RectangleExtensions
{
    public static Rectangle Inflate(this Rectangle rect, float left, float top, float right, float bottom) =>
        new(rect.X - left, rect.Y - top, rect.Width + left + right, rect.Height + top + bottom);
}