using System.Text;
using Raylib_cs;
using System.Globalization;
using System.Collections.Generic;
using System.Linq;
using System;

namespace Karpik.Engine.Client.UIToolkit;

public enum LayoutContext { Block, FlexItem }

public class LayoutEngine
{
    private List<UIElement> _absoluteElements;
    private List<UIElement> _fixedElements;

    public void Layout(UIElement root, Rectangle viewport, Font defaultFont)
    {
        Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;
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

        float availableWidth = containingBlock.Width - mLeft - mRight;
        float availableHeight = containingBlock.Height - mTop - mBottom;

        var widthVal = ParseValue(element.ComputedStyle.GetValueOrDefault("width", "auto"));
        var heightVal = ParseValue(element.ComputedStyle.GetValueOrDefault("height", "auto"));

        float contentWidth;
        float? resolvedWidth = ResolveSize(widthVal, availableWidth, pLeft + pRight + bLeft + bRight);

        if (resolvedWidth.HasValue) contentWidth = resolvedWidth.Value;
        else
        {
            bool isParentRow = element.Parent != null && isRow(element.Parent);
            if (context == LayoutContext.FlexItem && isParentRow)
                contentWidth = CalculateNaturalSize(element, defaultFont, null).width;
            else contentWidth = availableWidth - pLeft - pRight - bLeft - bRight;
        }

        float contentHeight = ResolveSize(heightVal, availableHeight, pTop + pBottom + bTop + bBottom) ?? 0;
        contentWidth = Math.Max(0, contentWidth);

        float finalX, finalY;
        if (position == "static" || position == "relative")
        {
            finalX = containingBlock.X + mLeft;
            finalY = containingBlock.Y + mTop;
        }
        else
        {
            var lVal = ParseValue(element.ComputedStyle.GetValueOrDefault("left", "auto"));
            var rVal = ParseValue(element.ComputedStyle.GetValueOrDefault("right", "auto"));
            var tVal = ParseValue(element.ComputedStyle.GetValueOrDefault("top", "auto"));
            var bVal = ParseValue(element.ComputedStyle.GetValueOrDefault("bottom", "auto"));
            float leftOffset = lVal.Unit != Unit.Auto ? lVal.ToPx(containingBlock.Width) : float.NaN;
            float rightOffset = rVal.Unit != Unit.Auto ? rVal.ToPx(containingBlock.Width) : float.NaN;
            float topOffset = tVal.Unit != Unit.Auto ? tVal.ToPx(containingBlock.Height) : float.NaN;
            float bottomOffset = bVal.Unit != Unit.Auto ? bVal.ToPx(containingBlock.Height) : float.NaN;

            if (!float.IsNaN(leftOffset)) finalX = containingBlock.X + leftOffset;
            else if (!float.IsNaN(rightOffset))
                finalX = (containingBlock.X + containingBlock.Width) - rightOffset - mRight - (pRight + bRight) -
                         contentWidth;
            else finalX = containingBlock.X + mLeft;

            if (!float.IsNaN(topOffset)) finalY = containingBlock.Y + topOffset;
            else if (!float.IsNaN(bottomOffset))
                finalY = (containingBlock.Y + containingBlock.Height) - bottomOffset - mBottom - (pBottom + bBottom) -
                         contentHeight;
            else finalY = containingBlock.Y + mTop;
        }

        if (element.ComputedStyle.GetValueOrDefault("display") == "flex")
        {
            var borderBoxWidth = contentWidth + pLeft + pRight + bLeft + bRight;
            var borderBoxHeight = contentHeight + pTop + pBottom + bTop + bBottom;
            RunFlexLayout(element, new Rectangle(finalX, finalY, borderBoxWidth, borderBoxHeight), defaultFont);
        }
        else
        {
            // --- ИЗМЕНЕНИЕ 1: Устанавливаем базовую высоту на основе СОБСТВЕННОГО текста ---
            float naturalHeight = CalculateNaturalSize(element, defaultFont, contentWidth).height;
            if (heightVal.Unit == Unit.Auto)
            {
                contentHeight = naturalHeight;
            }

            element.LayoutBox = new LayoutBox
            {
                ContentRect = new Rectangle(finalX + bLeft + pLeft, finalY + bTop + pTop, contentWidth, contentHeight)
            };
            RecalculateOuterRects(element, containingBlock);
            if (element.Children.Any())
            {
                LayoutBlockChildren(element, defaultFont);
            }
        }
    }

    private void LayoutBlockChildren(UIElement parent, Font defaultFont)
    {
        float currentY = 0;
        var childContainingBlock = parent.LayoutBox.ContentRect;
        float totalChildHeight = 0;

        foreach (var child in parent.Children)
        {
            LayoutNode(child, new Rectangle(), defaultFont, LayoutContext.Block);
            var childPosition = child.ComputedStyle.GetValueOrDefault("position", "static");
            if (childPosition != "static" && childPosition != "relative") continue;
            var itemContainingBlock = new Rectangle(childContainingBlock.X, childContainingBlock.Y + currentY,
                childContainingBlock.Width, Math.Max(0, childContainingBlock.Height - currentY));
            LayoutNode(child, itemContainingBlock, defaultFont, LayoutContext.Block);
            float childTotalHeight = child.LayoutBox.MarginRect.Height;
            currentY += childTotalHeight;
            totalChildHeight += childTotalHeight;
        }

        if (ParseValue(parent.ComputedStyle.GetValueOrDefault("height", "auto")).Unit == Unit.Auto)
        {
            // Используем высоту дочерних элементов, ТОЛЬКО если она больше естественной высоты родителя
            if (totalChildHeight > parent.LayoutBox.ContentRect.Height)
            {
                parent.LayoutBox.ContentRect = new Rectangle(parent.LayoutBox.ContentRect.X,
                    parent.LayoutBox.ContentRect.Y, parent.LayoutBox.ContentRect.Width, totalChildHeight);
                RecalculateOuterRects(parent, new Rectangle());
            }
        }
    }

    private void RunFlexLayout(UIElement element, Rectangle elementBox, Font defaultFont)
    {
        foreach (var child in element.Children) LayoutNode(child, new Rectangle(), defaultFont, LayoutContext.Block);
        var flexItems = element.Children.Where(c =>
            c.ComputedStyle.GetValueOrDefault("position", "static") == "static" ||
            c.ComputedStyle.GetValueOrDefault("position") == "relative").ToList();
        var (pLeft, pRight, pTop, pBottom) = ParseAllPaddings(element.ComputedStyle, elementBox);
        var (bLeft, bRight, bTop, bBottom) = ParseAllBorders(element.ComputedStyle, elementBox);
        bool isRow = this.isRow(element);
        var heightVal = ParseValue(element.ComputedStyle.GetValueOrDefault("height", "auto"));
        var widthVal = ParseValue(element.ComputedStyle.GetValueOrDefault("width", "auto"));
        float availableWidth = elementBox.Width - pLeft - pRight - bLeft - bRight;
        float availableHeight = elementBox.Height - pTop - pBottom - bTop - bBottom;
        float mainSize = isRow ? availableWidth : availableHeight;
        var baseSizes = new Dictionary<UIElement, float>();
        float totalBaseSize = 0;
        foreach (var item in flexItems)
        {
            LayoutNode(item,
                new Rectangle(0, 0, isRow ? float.PositiveInfinity : availableWidth,
                    isRow ? availableHeight : float.PositiveInfinity), defaultFont, LayoutContext.FlexItem);
            var basis = ParseValue(item.ComputedStyle.GetValueOrDefault("flex-basis", "auto"));
            float baseSize = basis.Unit == Unit.Auto
                ? (isRow ? item.LayoutBox.MarginRect.Width : item.LayoutBox.MarginRect.Height)
                : basis.ToPx(mainSize);
            baseSizes[item] = baseSize;
            totalBaseSize += baseSize;
        }

        float freeSpace = mainSize - totalBaseSize;
        var finalSizes = new Dictionary<UIElement, float>(baseSizes);
        if (freeSpace > 0)
        {
            float totalFlexGrow =
                flexItems.Sum(item => ParseFloat(item.ComputedStyle.GetValueOrDefault("flex-grow", "0")));
            if (totalFlexGrow > 0)
                foreach (var item in flexItems)
                    finalSizes[item] +=
                        (ParseFloat(item.ComputedStyle.GetValueOrDefault("flex-grow", "0")) / totalFlexGrow) *
                        freeSpace;
        }
        else if (freeSpace < 0)
        {
            float totalWeightedShrink = flexItems.Sum(item =>
                ParseFloat(item.ComputedStyle.GetValueOrDefault("flex-shrink", "1")) * baseSizes[item]);
            if (totalWeightedShrink > 0)
                foreach (var item in flexItems)
                    finalSizes[item] +=
                        ((ParseFloat(item.ComputedStyle.GetValueOrDefault("flex-shrink", "1")) * baseSizes[item]) /
                         totalWeightedShrink) * freeSpace;
        }

        var naturalCrossSizes = new Dictionary<UIElement, float>();
        float maxCrossSize = 0;
        foreach (var item in flexItems)
        {
            var (imL, imR, imT, imB) = ParseAllMargins(item.ComputedStyle, elementBox);
            var (ibL, ibR, ibT, ibB) = ParseAllBorders(item.ComputedStyle, elementBox);
            var (ipL, ipR, ipT, ipB) = ParseAllPaddings(item.ComputedStyle, elementBox);
            float naturalCrossSize;
            if (isRow)
            {
                float itemContentWidth = finalSizes[item] - imL - imR - ibL - ibR - ipL - ipR;
                var naturalHeight = CalculateNaturalSize(item, defaultFont, itemContentWidth).height;
                naturalCrossSize = naturalHeight + ipT + ipB + ibT + ibB + imT + imB;
            }
            else naturalCrossSize = item.LayoutBox.MarginRect.Width;

            naturalCrossSizes[item] = naturalCrossSize;
            if (naturalCrossSize > maxCrossSize) maxCrossSize = naturalCrossSize;
        }

        float finalContentWidth = isRow ? availableWidth : (widthVal.Unit == Unit.Auto ? maxCrossSize : availableWidth);
        float finalContentHeight =
            isRow ? (heightVal.Unit == Unit.Auto ? maxCrossSize : availableHeight) : availableHeight;
        element.LayoutBox = new LayoutBox
        {
            ContentRect = new Rectangle(elementBox.X + bLeft + pLeft, elementBox.Y + bTop + pTop, finalContentWidth,
                finalContentHeight)
        };
        RecalculateOuterRects(element, new Rectangle(0, 0, elementBox.Width, elementBox.Height));
        var justifyContent = element.ComputedStyle.GetValueOrDefault("justify-content", "flex-start");
        float actualUsedSpace = finalSizes.Values.Sum(s => Math.Max(0, s));
        float actualFreeSpace = (isRow ? finalContentWidth : finalContentHeight) - actualUsedSpace;
        float initialOffset = 0;
        if (actualFreeSpace > 0)
        {
            if (justifyContent == "center") initialOffset = actualFreeSpace / 2;
            else if (justifyContent == "flex-end") initialOffset = actualFreeSpace;
        }

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
            float crossSizeContainer = isRow ? finalContentHeight : finalContentWidth;
            float itemFinalCrossSize;
            var itemCrossAxisDimension = isRow ? "height" : "width";
            if (effectiveAlign == "stretch" &&
                ParseValue(item.ComputedStyle.GetValueOrDefault(itemCrossAxisDimension, "auto")).Unit == Unit.Auto)
                itemFinalCrossSize = crossSizeContainer;
            else itemFinalCrossSize = naturalCrossSizes[item];
            float crossOffset = 0;
            if (effectiveAlign == "center") crossOffset = (crossSizeContainer - itemFinalCrossSize) / 2;
            else if (effectiveAlign == "flex-end") crossOffset = crossSizeContainer - itemFinalCrossSize;
            float childX = element.LayoutBox.ContentRect.X + (isRow ? currentMainPos : crossOffset);
            float childY = element.LayoutBox.ContentRect.Y + (isRow ? crossOffset : currentMainPos);
            var finalRect = new Rectangle(childX, childY, isRow ? finalMainSize : itemFinalCrossSize,
                isRow ? itemFinalCrossSize : finalMainSize);
            FinalizeItemLayout(item, finalRect, defaultFont);
            currentMainPos += finalMainSize + spaceBetween;
        }
    }

    private void FinalizeItemLayout(UIElement item, Rectangle finalMarginRect, Font defaultFont)
    {
        var (imL, imR, imT, imB) = ParseAllMargins(item.ComputedStyle, finalMarginRect);
        var (ipL, ipR, ipT, ipB) = ParseAllPaddings(item.ComputedStyle, finalMarginRect);
        var (ibL, ibR, ibT, ibB) = ParseAllBorders(item.ComputedStyle, finalMarginRect);
        item.LayoutBox = new LayoutBox { MarginRect = finalMarginRect };
        item.LayoutBox.BorderRect = new Rectangle(finalMarginRect.X + imL, finalMarginRect.Y + imT,
            Math.Max(0, finalMarginRect.Width - imL - imR), Math.Max(0, finalMarginRect.Height - imT - imB));
        item.LayoutBox.PaddingRect = new Rectangle(item.LayoutBox.BorderRect.X + ibL, item.LayoutBox.BorderRect.Y + ibT,
            Math.Max(0, item.LayoutBox.BorderRect.Width - ibL - ibR),
            Math.Max(0, item.LayoutBox.BorderRect.Height - ibT - ibB));
        item.LayoutBox.ContentRect = new Rectangle(item.LayoutBox.PaddingRect.X + ipL,
            item.LayoutBox.PaddingRect.Y + ipT, Math.Max(0, item.LayoutBox.PaddingRect.Width - ipL - ipR),
            Math.Max(0, item.LayoutBox.PaddingRect.Height - ipT - ipB));
        if (item.Children.Any())
        {
            if (item.ComputedStyle.GetValueOrDefault("display") == "flex")
                RunFlexLayout(item, item.LayoutBox.BorderRect, defaultFont);
            else LayoutBlockChildren(item, defaultFont);
        }
        else if (!string.IsNullOrEmpty(item.Text))
        {
            CalculateNaturalSize(item, defaultFont, item.LayoutBox.ContentRect.Width);
        }
    }

    // --- ИЗМЕНЕНИЕ 2: CalculateNaturalSize теперь заглядывает в дочерние элементы ---
    private (float width, float height) CalculateNaturalSize(UIElement element, Font defaultFont, float? wrapWidth = null)
    {
        if (element.Text == "3" || element.Classes.Contains("notification-badge"))
        {
            
        }
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

        // --- НАЧАЛО ИСПРАВЛЕННОЙ ЛОГИКИ ---
        // Проверяем детей, которые участвуют в потоке (static ИЛИ relative)
        var flowChildren = element.Children
            .Where(c =>
            {
                var pos = c.ComputedStyle.GetValueOrDefault("position", "static");
                return (pos == "static" || pos == "relative") && c.ComputedStyle.GetValueOrDefault("display") != "none";
            })
            .ToList();

        if (textWidth == 0 && flowChildren.Any())
        {
            float maxChildWidth = 0;
            // Эвристика: естественная ширина контейнера - это ширина самого широкого дочернего элемента
            foreach (var child in flowChildren)
            {
                // Рекурсивно измеряем детей
                var childSize = CalculateNaturalSize(child, defaultFont, null);
                if (childSize.width > maxChildWidth)
                {
                    maxChildWidth = childSize.width;
                }
            }

            textWidth = maxChildWidth;
        }
        // --- КОНЕЦ ИСПРАВЛЕННОЙ ЛОГИКИ ---

        return (textWidth, textHeight);
    }

    // Вспомогательные методы без изменений
    private float? ResolveSize(StyleValue val, float baseValue, float borderAndPadding)
    {
        if (val.Unit == Unit.Auto) return null;
        return val.ToPx(baseValue);
    }

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
        if (e.Text == "3" || e.Classes.Contains("notification-badge"))
        {
            
        }
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
}

#region Extension Classes and Structs
public static class StyleValueExtensions{public static float ToPx(this StyleValue val,float baseValue,float defaultValue=0f)=>val.Unit switch{Unit.Auto=>defaultValue,Unit.Percent when float.IsInfinity(baseValue)=>defaultValue,Unit.Percent=>(val.Value/100f)*baseValue,_=>val.Value};}
public static class RectangleExtensions{public static Rectangle Inflate(this Rectangle rect,float left,float top,float right,float bottom)=>new(rect.X-left,rect.Y-top,rect.Width+left+right,rect.Height+top+bottom);}
#endregion