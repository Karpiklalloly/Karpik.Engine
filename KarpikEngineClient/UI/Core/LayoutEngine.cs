using System.Text;
using Raylib_cs;

namespace Karpik.Engine.Client.UIToolkit;

public enum LayoutType
{
    Block,
    InlineBlock
}

public enum LayoutContext { Block, FlexItem } // Новый enum для контекста

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
            element.LayoutBox = new LayoutBox { MarginRect = new Rectangle(containingBlock.X, containingBlock.Y, 0, 0) };
            return;
        }
        
        var (marginLeft, marginRight, marginTop, marginBottom) = ParseAllMargins(style, containingBlock);
        var (paddingLeft, paddingRight, paddingTop, paddingBottom) = ParseAllPaddings(style, containingBlock);
        var (borderLeft, borderRight, borderTop, borderBottom) = ParseAllBorders(style, containingBlock);
        
        var (contentWidth, contentHeight) = CalculateNodeSize(element, containingBlock, context, defaultFont,
            marginTop, marginBottom, marginLeft, marginRight,
            borderTop, borderBottom, borderLeft, borderRight,
            paddingTop, paddingBottom, paddingLeft, paddingRight);

        float staticX = containingBlock.X + marginLeft;
        float staticY = containingBlock.Y + marginTop;
        float finalX = staticX, finalY = staticY;
        
        var top = ParseValue(style.GetValueOrDefault("top", "auto")).ToPx(containingBlock.Height, float.NaN);
        var bottom = ParseValue(style.GetValueOrDefault("bottom", "auto")).ToPx(containingBlock.Height, float.NaN);
        var left = ParseValue(style.GetValueOrDefault("left", "auto")).ToPx(containingBlock.Width, float.NaN);
        var right = ParseValue(style.GetValueOrDefault("right", "auto")).ToPx(containingBlock.Width, float.NaN);

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
            else if (!float.IsNaN(right)) finalX = containingBlock.X + containingBlock.Width - right - (contentWidth + paddingLeft + paddingRight + borderLeft + borderRight + marginRight);
            if (!float.IsNaN(top)) finalY = containingBlock.Y + top;
        }

        if (display == "flex")
        {
            var flexItems = element.Children.Where(c => c.ComputedStyle.GetValueOrDefault("position", "static") == "static").ToList();
            var flexDirection = style.GetValueOrDefault("flex-direction", "row");
            bool isRow = flexDirection.StartsWith("row");
            
            float totalFlexGrow = 0;
            float totalChildrenSizeMainAxis = 0;

            foreach (var item in flexItems)
            {
                LayoutNode(item, new Rectangle(0, 0, contentWidth, contentHeight), defaultFont, LayoutContext.FlexItem);
                totalFlexGrow += ParseFloat(item.ComputedStyle.GetValueOrDefault("flex-grow", "0"));
                totalChildrenSizeMainAxis += isRow ? item.LayoutBox.MarginRect.Width : item.LayoutBox.MarginRect.Height;
            }
            
            if (element.ComputedStyle.GetValueOrDefault("height") == "auto" && isRow)
            {
                float maxChildHeight = 0;
                if (flexItems.Any()) maxChildHeight = flexItems.Max(item => item.LayoutBox.MarginRect.Height);
                contentHeight = maxChildHeight;
            }
            else if(element.ComputedStyle.GetValueOrDefault("height") == "auto" && !isRow)
            {
                contentHeight = totalChildrenSizeMainAxis;
            }
            
            float containerMainSize = isRow ? contentWidth : contentHeight;
            float freeSpace = containerMainSize - totalChildrenSizeMainAxis;
            float spacePerGrowUnit = totalFlexGrow > 0 && freeSpace > 0 ? freeSpace / totalFlexGrow : 0;
            
            if (spacePerGrowUnit > 0)
            {
                foreach (var item in flexItems)
                {
                    var grow = ParseFloat(item.ComputedStyle.GetValueOrDefault("flex-grow", "0"));
                    if (grow > 0)
                    {
                        var itemGrowSize = grow * spacePerGrowUnit;
                        var box = item.LayoutBox;
                        if (isRow) box.ContentRect = new Rectangle(box.ContentRect.X, box.ContentRect.Y, box.ContentRect.Width + itemGrowSize, box.ContentRect.Height);
                        else box.ContentRect = new Rectangle(box.ContentRect.X, box.ContentRect.Y, box.ContentRect.Width, box.ContentRect.Height + itemGrowSize);
                        RecalculateOuterRects(item);
                    }
                }
            }
            
            totalChildrenSizeMainAxis = 0;
            foreach (var item in flexItems) totalChildrenSizeMainAxis += isRow ? item.LayoutBox.MarginRect.Width : item.LayoutBox.MarginRect.Height;
            freeSpace = containerMainSize - totalChildrenSizeMainAxis;
            
            var justifyContent = style.GetValueOrDefault("justify-content", "flex-start");
            var alignItems = style.GetValueOrDefault("align-items", "stretch");
            float mainAxisOffset = 0;
            
            if (justifyContent == "center") mainAxisOffset = freeSpace / 2;
            if (justifyContent == "flex-end") mainAxisOffset = freeSpace;
            
            float spaceBetween = 0;
            if (justifyContent == "space-between" && flexItems.Count > 1 && freeSpace > 0)
            {
                spaceBetween = freeSpace / (flexItems.Count - 1);
            }
            
            foreach (var item in flexItems)
            {
                var box = item.LayoutBox;
                float crossAxisOffset = 0;
                float containerCrossSize = isRow ? contentHeight : contentWidth;
                float itemCrossSize = isRow ? box.MarginRect.Height : box.MarginRect.Width;
                
                if (alignItems == "center") crossAxisOffset = (containerCrossSize - itemCrossSize) / 2;
                else if (alignItems == "flex-end") crossAxisOffset = containerCrossSize - itemCrossSize;
                else if (alignItems == "stretch" && (isRow ? item.ComputedStyle.GetValueOrDefault("height") == "auto" : item.ComputedStyle.GetValueOrDefault("width") == "auto"))
                {
                     if (isRow)
                     {
                         box.ContentRect = new Rectangle(box.ContentRect.X, box.ContentRect.Y, box.ContentRect.Width,
                             containerCrossSize - (box.MarginRect.Height - box.ContentRect.Height));
                     }
                     else
                     {
                         box.ContentRect = new Rectangle(box.ContentRect.X, box.ContentRect.Y,
                             containerCrossSize - (box.MarginRect.Width - box.ContentRect.Width),
                             box.ContentRect.Height);
                     }
                     RecalculateOuterRects(item);
                }

                if (isRow) box.Shift(finalX + borderLeft + paddingLeft + mainAxisOffset, finalY + borderTop + paddingTop + crossAxisOffset);
                else box.Shift(finalX + borderLeft + paddingLeft + crossAxisOffset, finalY + borderTop + paddingTop + mainAxisOffset);
                
                mainAxisOffset += isRow ? item.LayoutBox.MarginRect.Width : item.LayoutBox.MarginRect.Height;
                if (justifyContent == "space-between") mainAxisOffset += spaceBetween;
            }
        }
        else // Блочная компоновка
        {
            var childContainingBlock = new Rectangle(finalX + borderLeft + paddingLeft, finalY + borderTop + paddingTop, contentWidth, float.PositiveInfinity);
            float childrenInFlowHeight = 0, currentLineX = 0, currentLineHeight = 0;
            
            foreach (var child in element.Children.Where(c => c.ComputedStyle.GetValueOrDefault("position", "static") == "static"))
            {
                var childContext = child.ComputedStyle.GetValueOrDefault("display", "block") == "inline-block" ? LayoutContext.FlexItem : LayoutContext.Block;
                
                Rectangle currentChildBlock;
                var (childWidth, _) = CalculateNodeSize(child, childContainingBlock, LayoutContext.FlexItem, defaultFont, 0,0,0,0,0,0,0,0,0,0,0,0);
                var (childMarginLeft, childMarginRight, _, _) = ParseAllMargins(child.ComputedStyle, childContainingBlock);
                float childWidthWithMargin = childWidth + childMarginLeft + childMarginRight;
                
                if (childContext == LayoutContext.FlexItem && (currentLineX + childWidthWithMargin > childContainingBlock.Width) && currentLineX > 0)
                {
                    childrenInFlowHeight += currentLineHeight;
                    currentLineHeight = 0;
                    currentLineX = 0;
                }

                if (childContext == LayoutContext.FlexItem)
                {
                    currentChildBlock = new Rectangle(childContainingBlock.X + currentLineX, childContainingBlock.Y + childrenInFlowHeight, childContainingBlock.Width - currentLineX, childContainingBlock.Height);
                }
                else
                {
                    childrenInFlowHeight += currentLineHeight;
                    currentLineHeight = 0;
                    currentLineX = 0;
                    currentChildBlock = new Rectangle(childContainingBlock.X, childContainingBlock.Y + childrenInFlowHeight, childContainingBlock.Width, childContainingBlock.Height);
                }
                
                LayoutNode(child, currentChildBlock, defaultFont, childContext);
                
                if (childContext == LayoutContext.FlexItem)
                {
                    currentLineX += child.LayoutBox.MarginRect.Width;
                    currentLineHeight = Math.Max(currentLineHeight, child.LayoutBox.MarginRect.Height);
                }
                else
                {
                    childrenInFlowHeight += child.LayoutBox.MarginRect.Height;
                }
            }
            childrenInFlowHeight += currentLineHeight;
            if (element.ComputedStyle.GetValueOrDefault("height") == "auto")
            {
                contentHeight = Math.Max(contentHeight, childrenInFlowHeight);
            }
        }
        
        if (position == "absolute" || position == "fixed")
        {
            if (!float.IsNaN(bottom) && float.IsNaN(top))
            {
                finalY = containingBlock.Y + containingBlock.Height - bottom - (contentHeight + paddingTop + paddingBottom + borderTop + borderBottom + marginBottom);
            }
        }

        var box2 = new LayoutBox();
        box2.ContentRect = new Rectangle(finalX + borderLeft + paddingLeft, finalY + borderTop + paddingTop, contentWidth, contentHeight);
        box2.PaddingRect = box2.ContentRect.Inflate(paddingLeft, paddingTop, paddingRight, paddingBottom);
        box2.BorderRect = box2.PaddingRect.Inflate(borderLeft, borderTop, borderRight, borderBottom);
        box2.MarginRect = box2.BorderRect.Inflate(marginLeft, marginTop, marginRight, marginBottom);
        element.LayoutBox = box2;
    }
    
    private (float contentWidth, float contentHeight) CalculateNodeSize(
        UIElement element, Rectangle containingBlock, LayoutContext context, Font defaultFont,
        float marginTop, float marginBottom, float marginLeft, float marginRight,
        float borderTop, float borderBottom, float borderLeft, float borderRight,
        float paddingTop, float paddingBottom, float paddingLeft, float paddingRight)
    {
        var style = element.ComputedStyle;
        var widthVal = ParseValue(style.GetValueOrDefault("width", "auto"));
        var heightVal = ParseValue(style.GetValueOrDefault("height", "auto"));
        bool isBorderBox = style.GetValueOrDefault("box-sizing") == "border-box";
        
        float contentWidth = 0;
        if (widthVal.Unit == Unit.Auto)
        {
            // Элемент должен сжиматься до содержимого только если он inline-block.
            // Во всех остальных случаях (block, flex, flex-item) он по умолчанию занимает всю доступную ширину.
            if (style.GetValueOrDefault("display") == "inline-block")
            {
                contentWidth = 0;
                if (!string.IsNullOrEmpty(element.Text))
                {
                    var fontSize = ParseValue(style.GetValueOrDefault("font-size", "16")).ToPx(0);
                    // Измеряем "естественную" ширину текста без переноса строк
                    WrapText(element, float.PositiveInfinity, defaultFont, fontSize);
                    if (element.WrappedTextLines.Any())
                        contentWidth = element.WrappedTextLines.Max(line => Raylib.MeasureTextEx(defaultFont, line, fontSize, 1).X);
                }
            }
            else // Для display: block, display: flex и для flex-элементов (которые ведут себя как block по поперечной оси)
            {
                contentWidth = Math.Max(0, containingBlock.Width - (marginLeft + marginRight + borderLeft + borderRight + paddingLeft + paddingRight));
            }
        }
        else
        {
            float totalWidth = widthVal.ToPx(containingBlock.Width);
            contentWidth = isBorderBox 
                ? Math.Max(0, totalWidth - borderLeft - borderRight - paddingLeft - paddingRight) 
                : totalWidth;
        }

        float contentHeight = 0;
        if (heightVal.Unit == Unit.Auto)
        {
            
        }
        else
        {
            float totalHeight = heightVal.ToPx(containingBlock.Height);
            contentHeight = isBorderBox 
                ? Math.Max(0, totalHeight - borderTop - borderBottom - paddingTop - paddingBottom) 
                : totalHeight;
        }
        
        if (!string.IsNullOrEmpty(element.Text))
        {
            var fontSize = ParseValue(style.GetValueOrDefault("font-size", "16")).ToPx(0);
            // Для текста: используем contentWidth, который уже должен быть корректно рассчитан.
            WrapText(element, contentWidth, defaultFont, fontSize); 
            if (heightVal.Unit == Unit.Auto)
            {
                var lineHeight = ParseValue(style.GetValueOrDefault("line-height", "auto")).ToPx(fontSize, fontSize * 1.2f);
                contentHeight = element.WrappedTextLines.Count * lineHeight;
            }
        }
        
        var minWidth = ParseValue(style.GetValueOrDefault("min-width", "0px")).ToPx(containingBlock.Width);
        var maxWidth = ParseValue(style.GetValueOrDefault("max-width", "auto")).ToPx(containingBlock.Width, float.PositiveInfinity);
        contentWidth = Math.Clamp(contentWidth, minWidth, maxWidth);
        
        var minHeight = ParseValue(style.GetValueOrDefault("min-height", "0px")).ToPx(containingBlock.Height);
        var maxHeight = ParseValue(style.GetValueOrDefault("max-height", "auto")).ToPx(containingBlock.Height, float.PositiveInfinity);
        contentHeight = Math.Clamp(contentHeight, minHeight, maxHeight);

        return (contentWidth, contentHeight);
    }
    
    private void RecalculateOuterRects(UIElement element)
    {
        var style = element.ComputedStyle;
        var box = element.LayoutBox;
        var parentContentBox = element.Parent?.LayoutBox.ContentRect ?? element.LayoutBox.ContentRect;
        var (marginLeft, marginRight, marginTop, marginBottom) = ParseAllMargins(style, parentContentBox);
        var (paddingLeft, paddingRight, paddingTop, paddingBottom) = ParseAllPaddings(style, parentContentBox);
        var (borderLeft, borderRight, borderTop, borderBottom) = ParseAllBorders(style, parentContentBox);
        
        box.PaddingRect = box.ContentRect.Inflate(paddingLeft, paddingTop, paddingRight, paddingBottom);
        box.BorderRect = box.PaddingRect.Inflate(borderLeft, borderTop, borderRight, borderBottom);
        box.MarginRect = box.BorderRect.Inflate(marginLeft, marginTop, marginRight, marginBottom);
    }

    private UIElement FindOffsetParent(UIElement element)
    {
        var parent = element.Parent;
        while (parent != null)
        {
            var position = parent.ComputedStyle.GetValueOrDefault("position", "static");
            if (position == "relative" || position == "absolute" || position == "fixed") return parent;
            parent = parent.Parent;
        }
        return null;
    }

    private UIElement FindRoot(UIElement element)
    {
        while (element.Parent != null) element = element.Parent;
        return element;
    }
    
    #region Парсеры
    private StyleValue ParseValue(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return StyleValue.Px(0);
        value = value.Trim();
        if (value == "auto") return StyleValue.Auto;
        if (value.EndsWith("px")) return StyleValue.Px(float.Parse(value.Substring(0, value.Length - 2)));
        if (value.EndsWith("%")) return StyleValue.Percent(float.Parse(value.Substring(0, value.Length - 1)));
        if (float.TryParse(value, out float num)) return StyleValue.Px(num);
        return StyleValue.Px(0);
    }

    private Edges ParseEdges(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return new Edges();
        var parts = value.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).Select(ParseValue).ToArray();
        var result = new Edges();
        switch (parts.Length)
        {
            case 1: result.Top = result.Right = result.Bottom = result.Left = parts[0]; break;
            case 2: result.Top = result.Bottom = parts[0]; result.Right = result.Left = parts[1]; break;
            case 3: result.Top = parts[0]; result.Right = result.Left = parts[1]; result.Bottom = parts[2]; break;
            case 4: result.Top = parts[0]; result.Right = parts[1]; result.Bottom = parts[2]; result.Left = parts[3]; break;
        }
        return result;
    }

    private float ParseFloat(string value, float defaultValue = 0f)
    {
        if (string.IsNullOrWhiteSpace(value)) return defaultValue;
        value = value.Replace("px", "").Trim();
        if (float.TryParse(value, out float result)) return result;
        return defaultValue;
    }

    private (float, float, float, float) ParseAllMargins(Dictionary<string, string> style, Rectangle containingBlock)
    {
        var margin = ParseEdges(style.GetValueOrDefault("margin", "0"));
        float marginLeft = margin.Left.ToPx(containingBlock.Width);
        float marginRight = margin.Right.ToPx(containingBlock.Width);
        float marginTop = margin.Top.ToPx(containingBlock.Height);
        float marginBottom = margin.Bottom.ToPx(containingBlock.Height);
        return (marginLeft, marginRight, marginTop, marginBottom);
    }

    private (float, float, float, float) ParseAllPaddings(Dictionary<string, string> style, Rectangle containingBlock)
    {
        var padding = ParseEdges(style.GetValueOrDefault("padding", "0"));
        float paddingLeft = padding.Left.ToPx(containingBlock.Width);
        float paddingRight = padding.Right.ToPx(containingBlock.Width);
        float paddingTop = padding.Top.ToPx(containingBlock.Height);
        float paddingBottom = padding.Bottom.ToPx(containingBlock.Height);
        return (paddingLeft, paddingRight, paddingTop, paddingBottom);
    }

    private (float, float, float, float) ParseAllBorders(Dictionary<string, string> style, Rectangle containingBlock)
    {
        var border = ParseEdges(style.GetValueOrDefault("border-width", "0"));
        float borderLeft = border.Left.ToPx(containingBlock.Width);
        float borderRight = border.Right.ToPx(containingBlock.Width);
        float borderTop = border.Top.ToPx(containingBlock.Height);
        float borderBottom = border.Bottom.ToPx(containingBlock.Height);
        return (borderLeft, borderRight, borderTop, borderBottom);
    }
    
    private void WrapText(UIElement element, float maxWidth, Font font, float fontSize)
    {
        element.WrappedTextLines.Clear();
        if (string.IsNullOrEmpty(element.Text)) return;
        if (maxWidth <= 0)
        {
            if(element.Text.Contains(" ")) element.WrappedTextLines.AddRange(element.Text.Split(' '));
            else element.WrappedTextLines.Add(element.Text);
            return;
        }
        var words = element.Text.Split(' ');
        var currentLine = new StringBuilder();
        foreach (var word in words)
        {
            var testLine = currentLine.Length > 0 ? currentLine.ToString() + " " + word : word;
            var dimensions = Raylib.MeasureTextEx(font, testLine, fontSize, 1);
            if (dimensions.X > maxWidth && currentLine.Length > 0)
            {
                element.WrappedTextLines.Add(currentLine.ToString());
                currentLine.Clear().Append(word);
            }
            else
            {
                if (currentLine.Length > 0) currentLine.Append(" ");
                currentLine.Append(word);
            }
        }
        if (currentLine.Length > 0)
        {
            element.WrappedTextLines.Add(currentLine.ToString());
        }
    }
    #endregion
}

#region Классы-расширения
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
    public static Rectangle Inflate(this Rectangle rect, float left, float top, float right, float bottom)
    {
        return new Rectangle(rect.X - left, rect.Y - top, rect.Width + left + right, rect.Height + top + bottom);
    }
}

public static class LayoutBoxExtensions
{
    public static void Shift(this LayoutBox box, float dx, float dy)
    {
        box.MarginRect = new Rectangle(box.MarginRect.X + dx, box.MarginRect.Y + dy, box.MarginRect.Width, box.MarginRect.Height);
        box.BorderRect = new Rectangle(box.BorderRect.X + dx, box.BorderRect.Y + dy, box.BorderRect.Width, box.BorderRect.Height);
        box.PaddingRect = new Rectangle(box.PaddingRect.X + dx, box.PaddingRect.Y + dy, box.PaddingRect.Width, box.PaddingRect.Height);
        box.ContentRect = new Rectangle(box.ContentRect.X + dx, box.ContentRect.Y + dy, box.ContentRect.Width, box.ContentRect.Height);
    }
}
#endregion