using System.Text;
using Raylib_cs;

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
        var style = element.ComputedStyle;
        var display = style.GetValueOrDefault("display", "block");

        if (display == "none")
        {
            element.LayoutBox = new LayoutBox { ContentRect = new Rectangle(0, 0, 0, 0) };
            return;
        }

        var position = style.GetValueOrDefault("position", "static");
        if ((position == "absolute" && !_absoluteElements.Contains(element)) || (position == "fixed" && !_fixedElements.Contains(element)))
        {
            if (position == "absolute") _absoluteElements.Add(element);
            if (position == "fixed") _fixedElements.Add(element);
            return;
        }
        
        var (marginLeft, marginRight, marginTop, marginBottom) = ParseAllMargins(style, containingBlock);
        var (paddingLeft, paddingRight, paddingTop, paddingBottom) = ParseAllPaddings(style, containingBlock);
        var (borderLeft, borderRight, borderTop, borderBottom) = ParseAllBorders(style, containingBlock);
        
        // ======================== НОВАЯ ЛОГИКА ОПРЕДЕЛЕНИЯ РАЗМЕРОВ ========================
        var widthVal = ParseValue(style.GetValueOrDefault("width", "auto"));
        var heightVal = ParseValue(style.GetValueOrDefault("height", "auto"));
        bool isBorderBox = style.GetValueOrDefault("box-sizing") == "border-box";
        
        float contentWidth, contentHeight;

        // --- Определяем ширину ---
        if (widthVal.Unit == Unit.Auto)
        {
            // Если родитель дал нам конечную ширину, мы её используем
            if (containingBlock.Width < float.PositiveInfinity && context != LayoutContext.FlexItem)
            {
                contentWidth = containingBlock.Width - marginLeft - marginRight - borderLeft - borderRight - paddingLeft - paddingRight;
            }
            else // Иначе измеряем "естественную" ширину
            {
                contentWidth = CalculateNaturalSize(element, defaultFont).width;
            }
        }
        else // Если ширина задана явно (px или %)
        {
            float totalWidth = widthVal.ToPx(containingBlock.Width);
            contentWidth = isBorderBox ? (totalWidth - borderLeft - borderRight - paddingLeft - paddingRight) : totalWidth;
        }

        // --- Определяем высоту ---
        if (heightVal.Unit == Unit.Auto)
        {
            // Если родитель дал нам конечную высоту (как при align-self: stretch), мы её используем
            if (containingBlock.Height < float.PositiveInfinity)
            {
                contentHeight = containingBlock.Height - marginTop - marginBottom - borderTop - borderBottom - paddingTop - paddingBottom;
            }
            else // Иначе измеряем "естественную" высоту
            {
                contentHeight = CalculateNaturalSize(element, defaultFont, contentWidth).height;
            }
        }
        else // Если высота задана явно (px или %)
        {
            float totalHeight = heightVal.ToPx(containingBlock.Height);
            contentHeight = isBorderBox ? (totalHeight - borderTop - borderBottom - paddingTop - paddingBottom) : totalHeight;
        }
        
        // Применяем min/max
        var minWidth = ParseValue(style.GetValueOrDefault("min-width", "0px")).ToPx(containingBlock.Width);
        contentWidth = Math.Max(contentWidth, minWidth);
        var minHeight = ParseValue(style.GetValueOrDefault("min-height", "0px")).ToPx(containingBlock.Height);
        contentHeight = Math.Max(contentHeight, minHeight);
        
        contentWidth = Math.Max(0, contentWidth);
        contentHeight = Math.Max(0, contentHeight);
        // =================================================================================

        float finalX = containingBlock.X + marginLeft;
        float finalY = containingBlock.Y + marginTop;

        element.LayoutBox.ContentRect = new Rectangle(finalX + borderLeft + paddingLeft, finalY + borderTop + paddingTop, contentWidth, contentHeight);
        RecalculateOuterRects(element, containingBlock);

        if (display == "flex")
        {
            var flexItems = element.Children.Where(c => c.ComputedStyle.GetValueOrDefault("position", "static") == "static").ToList();
            if (!flexItems.Any()) return;

            var flexDirection = style.GetValueOrDefault("flex-direction", "row");
            bool isRow = flexDirection.StartsWith("row");

            foreach (var item in flexItems)
            {
                var unconstrainedBlock = new Rectangle(0, 0, isRow ? float.PositiveInfinity : contentWidth, isRow ? contentHeight : float.PositiveInfinity);
                LayoutNode(item, unconstrainedBlock, defaultFont, LayoutContext.FlexItem);
            }

            if (isRow)
            {
                if (style.GetValueOrDefault("height", "auto") == "auto") contentHeight = flexItems.Any() ? flexItems.Max(item => item.LayoutBox.MarginRect.Height) : 0;
            }
            else 
            {
                if (style.GetValueOrDefault("height", "auto") == "auto") contentHeight = flexItems.Any() ? flexItems.Sum(item => item.LayoutBox.MarginRect.Height) : 0;
            }
            
            element.LayoutBox.ContentRect = new Rectangle(element.LayoutBox.ContentRect.X, element.LayoutBox.ContentRect.Y, contentWidth, contentHeight);
            RecalculateOuterRects(element, containingBlock);

            var baseSizes = new Dictionary<UIElement, float>();
            float totalBaseSize = 0, totalFlexGrow = 0;
            foreach (var item in flexItems)
            {
                var basisValue = ParseValue(item.ComputedStyle.GetValueOrDefault("flex-basis", "auto"));
                float baseSize = basisValue.Unit != Unit.Auto ? basisValue.ToPx(isRow ? contentWidth : contentHeight) : (isRow ? item.LayoutBox.MarginRect.Width : item.LayoutBox.MarginRect.Height);
                baseSizes[item] = baseSize;
                totalBaseSize += baseSize;
                totalFlexGrow += ParseFloat(item.ComputedStyle.GetValueOrDefault("flex-grow", "0"));
            }
            float containerMainSize = isRow ? contentWidth : contentHeight;
            float freeSpace = containerMainSize - totalBaseSize;
            var finalSizes = new Dictionary<UIElement, float>(baseSizes);
            if (freeSpace > 0 && totalFlexGrow > 0)
            {
                foreach (var item in flexItems)
                {
                    var grow = ParseFloat(item.ComputedStyle.GetValueOrDefault("flex-grow", "0"));
                    if (grow > 0) finalSizes[item] += (grow / totalFlexGrow) * freeSpace;
                }
            }
            else if (freeSpace < 0)
            {
                float totalWeightedShrink = flexItems.Sum(item => ParseFloat(item.ComputedStyle.GetValueOrDefault("flex-shrink", "1")) * baseSizes[item]);
                if (totalWeightedShrink > 0)
                {
                    foreach (var item in flexItems)
                    {
                        var shrink = ParseFloat(item.ComputedStyle.GetValueOrDefault("flex-shrink", "1"));
                        if (shrink > 0) finalSizes[item] -= ((shrink * baseSizes[item]) / totalWeightedShrink) * Math.Abs(freeSpace);
                    }
                }
            }
            
            var justifyContent = style.GetValueOrDefault("justify-content", "flex-start");
            var alignItems = style.GetValueOrDefault("align-items", "stretch");
            float actualFreeSpace = containerMainSize - finalSizes.Values.Sum();
            float initialMainAxisOffset = 0;
            if (actualFreeSpace > 0)
            {
                if (justifyContent == "center") initialMainAxisOffset = actualFreeSpace / 2;
                else if (justifyContent == "flex-end") initialMainAxisOffset = actualFreeSpace;
            }
            float spaceBetween = (justifyContent == "space-between" && flexItems.Count > 1 && actualFreeSpace > 0) ? actualFreeSpace / (flexItems.Count - 1) : 0;
            float currentMainAxisPos = initialMainAxisOffset;
            
            var childRects = new Dictionary<UIElement, Rectangle>();

            foreach (var item in flexItems)
            {
                var (itemMarginL, itemMarginR, itemMarginT, itemMarginB) = ParseAllMargins(item.ComputedStyle, element.LayoutBox.ContentRect);
                float finalMainSize = Math.Max(0, finalSizes[item]);
                
                float itemTotalWidth = isRow ? finalMainSize : item.LayoutBox.MarginRect.Width;
                float itemTotalHeight = !isRow ? finalMainSize : item.LayoutBox.MarginRect.Height;
                
                var alignSelf = item.ComputedStyle.GetValueOrDefault("align-self", "auto");
                var effectiveAlign = (alignSelf == "auto") ? alignItems : alignSelf;
                float containerCrossSize = isRow ? contentHeight : contentWidth;
                
                if (effectiveAlign == "stretch" && (isRow ? item.ComputedStyle.GetValueOrDefault("height", "auto") == "auto" : item.ComputedStyle.GetValueOrDefault("width", "auto") == "auto"))
                {
                    if (isRow) itemTotalHeight = containerCrossSize;
                    else itemTotalWidth = containerCrossSize;
                }
                
                float crossAxisOffset = 0;
                if (effectiveAlign == "center") crossAxisOffset = (containerCrossSize - (isRow ? itemTotalHeight : itemTotalWidth)) / 2;
                else if (effectiveAlign == "flex-end") crossAxisOffset = containerCrossSize - (isRow ? itemTotalHeight : itemTotalWidth);
                
                float childX = element.LayoutBox.ContentRect.X + (isRow ? currentMainAxisPos : crossAxisOffset) ;
                float childY = element.LayoutBox.ContentRect.Y + (isRow ? crossAxisOffset : currentMainAxisPos);
                
                childRects[item] = new Rectangle(childX, childY, itemTotalWidth, itemTotalHeight);

                currentMainAxisPos += isRow ? finalMainSize : finalMainSize;
                if (justifyContent == "space-between") currentMainAxisPos += spaceBetween;
            }
            
            foreach (var item in flexItems)
            {
                LayoutNode(item, childRects[item], defaultFont, LayoutContext.Block);
            }
        }
    }

    // Упрощенная функция для измерения "естественных" размеров по контенту
    private (float width, float height) CalculateNaturalSize(UIElement element, Font defaultFont, float? wrapWidth = null)
    {
        var style = element.ComputedStyle;
        float naturalWidth = 0;
        float naturalHeight = 0;

        if (!string.IsNullOrEmpty(element.Text))
        {
            var fontSize = ParseValue(style.GetValueOrDefault("font-size", "16")).ToPx(0);
            WrapText(element, wrapWidth ?? float.PositiveInfinity, defaultFont, fontSize);
            if (element.WrappedTextLines.Any())
            {
                naturalWidth = element.WrappedTextLines.Max(line => Raylib.MeasureTextEx(defaultFont, line, fontSize, 1).X);
                var lineHeight = ParseValue(style.GetValueOrDefault("line-height", "auto")).ToPx(fontSize, fontSize * 1.2f);
                naturalHeight = element.WrappedTextLines.Count * lineHeight;
            }
        }
        return (naturalWidth, naturalHeight);
    }
    
    private void RecalculateOuterRects(UIElement element, Rectangle parentContentBox)
    {
        var style = element.ComputedStyle;
        var (marginLeft, marginRight, marginTop, marginBottom) = ParseAllMargins(style, parentContentBox);
        var (paddingLeft, paddingRight, paddingTop, paddingBottom) = ParseAllPaddings(style, parentContentBox);
        var (borderLeft, borderRight, borderTop, borderBottom) = ParseAllBorders(style, parentContentBox);
        
        element.LayoutBox.PaddingRect = element.LayoutBox.ContentRect.Inflate(paddingLeft, paddingTop, paddingRight, paddingBottom);
        element.LayoutBox.BorderRect = element.LayoutBox.PaddingRect.Inflate(borderLeft, borderTop, borderRight, borderBottom);
        element.LayoutBox.MarginRect = element.LayoutBox.BorderRect.Inflate(marginLeft, marginTop, marginRight, marginBottom);
    }
    
    // ... (остальные вспомогательные методы без изменений)
    #region Unchanged Helper Methods
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
        if (value.EndsWith("px")) return StyleValue.Px(float.Parse(value[..^2]));
        if (value.EndsWith("%")) return StyleValue.Percent(float.Parse(value[..^1]));
        return float.TryParse(value, out float num) ? StyleValue.Px(num) : StyleValue.Px(0);
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
        return float.TryParse(value, out float result) ? result : defaultValue;
    }

    private (float, float, float, float) ParseAllMargins(Dictionary<string, string> style, Rectangle containingBlock)
    {
        var margin = ParseEdges(style.GetValueOrDefault("margin", "0"));
        return (margin.Left.ToPx(containingBlock.Width), margin.Right.ToPx(containingBlock.Width), margin.Top.ToPx(containingBlock.Height), margin.Bottom.ToPx(containingBlock.Height));
    }

    private (float, float, float, float) ParseAllPaddings(Dictionary<string, string> style, Rectangle containingBlock)
    {
        var padding = ParseEdges(style.GetValueOrDefault("padding", "0"));
        return (padding.Left.ToPx(containingBlock.Width), padding.Right.ToPx(containingBlock.Width), padding.Top.ToPx(containingBlock.Height), padding.Bottom.ToPx(containingBlock.Height));
    }

    private (float, float, float, float) ParseAllBorders(Dictionary<string, string> style, Rectangle containingBlock)
    {
        var border = ParseEdges(style.GetValueOrDefault("border-width", "0"));
        return (border.Left.ToPx(containingBlock.Width), border.Right.ToPx(containingBlock.Width), border.Top.ToPx(containingBlock.Height), border.Bottom.ToPx(containingBlock.Height));
    }
    
    private void WrapText(UIElement element, float maxWidth, Font font, float fontSize)
    {
        element.WrappedTextLines.Clear();
        if (string.IsNullOrEmpty(element.Text)) return;
        if (maxWidth <= 0) maxWidth = float.MaxValue;
        
        var words = element.Text.Split(' ');
        var currentLine = new StringBuilder();
        foreach (var word in words)
        {
            var testLine = currentLine.Length > 0 ? currentLine + " " + word : word;
            if (Raylib.MeasureTextEx(font, testLine, fontSize, 1).X > maxWidth && currentLine.Length > 0)
            {
                element.WrappedTextLines.Add(currentLine.ToString());
                currentLine.Clear().Append(word);
            }
            else
            {
                if (currentLine.Length > 0) currentLine.Append(' ');
                currentLine.Append(word);
            }
        }
        if (currentLine.Length > 0) element.WrappedTextLines.Add(currentLine.ToString());
    }
    #endregion
}

#region Extension Classes
public static class StyleValueExtensions
{
    public static float ToPx(this StyleValue val, float baseValue, float defaultValue = 0f)
    {
        if (val.Unit == Unit.Auto || (val.Unit == Unit.Percent && float.IsInfinity(baseValue)))
        {
            return defaultValue;
        }
        return val.Unit == Unit.Percent ? (val.Value / 100f) * baseValue : val.Value;
    }
}

public static class RectangleExtensions
{
    public static Rectangle Inflate(this Rectangle rect, float left, float top, float right, float bottom) => new(rect.X - left, rect.Y - top, rect.Width + left + right, rect.Height + top + bottom);
}
#endregion