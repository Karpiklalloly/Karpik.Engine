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
        
            var availableSpaceForAbsolute = containingBlock.LayoutBox.PaddingRect;
        
            Calculate(element, availableSpaceForAbsolute, defaultFont);
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
        if (parent.ComputedStyle.GetValueOrDefault(s.display) == s.display_none)
        {
            parent.LayoutBox = new LayoutBox();
            return;
        }
        
        var style = parent.ComputedStyle;
        var position = parent.GetPosition();
        
        
        var sizingBlock = (position == s.position_fixed) ? _viewport : availableSpace;

        var marginLeft = ParseValue(style.GetValueOrDefault(s.margin_left, "0")).ToPx(sizingBlock.Width);
        var marginRight = ParseValue(style.GetValueOrDefault(s.margin_right, "0")).ToPx(sizingBlock.Width);
        var borderLeft = ParseValue(style.GetValueOrDefault(s.border_left_width, "0")).ToPx(0);
        var borderRight = ParseValue(style.GetValueOrDefault(s.border_right_width, "0")).ToPx(0);
        var paddingLeft = ParseValue(style.GetValueOrDefault(s.padding_left, "0")).ToPx(0);
        var paddingRight = ParseValue(style.GetValueOrDefault(s.padding_right, "0")).ToPx(0);
        
        var marginTop = ParseValue(style.GetValueOrDefault(s.margin_top, "0")).ToPx(sizingBlock.Height);
        var marginBottom = ParseValue(style.GetValueOrDefault(s.margin_bottom, "0")).ToPx(sizingBlock.Height);
        var borderTop = ParseValue(style.GetValueOrDefault(s.border_top_width, "0")).ToPx(0);
        var borderBottom = ParseValue(style.GetValueOrDefault(s.border_bottom_width, "0")).ToPx(0);
        var paddingTop = ParseValue(style.GetValueOrDefault(s.padding_top, "0")).ToPx(0);
        var paddingBottom = ParseValue(style.GetValueOrDefault(s.padding_bottom, "0")).ToPx(0);
        
        var width = ParseValue(style.GetValueOrDefault(s.width, s.auto));
        var height = ParseValue(style.GetValueOrDefault(s.height, s.auto));
        
        float borderBoxWidth;

        if (width.Unit != Unit.Auto)
        {
            borderBoxWidth = width.ToPx(sizingBlock.Width);
        }
        else
        {
            if (float.IsInfinity(availableSpace.Width))
            {
                var naturalSize = CalculateNaturalSize(parent, font, null);
                borderBoxWidth = naturalSize.width + paddingLeft + paddingRight + borderLeft + borderRight;
            }
            else
            {
                borderBoxWidth = availableSpace.Width - marginLeft - marginRight;
            }
        }
        
        float contentWidth = borderBoxWidth - paddingLeft - paddingRight - borderLeft - borderRight;
        contentWidth = Math.Max(0, contentWidth);
        
        float finalX, finalY;
        if (position is s.position_static or s.position_relative)
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
                finalX = availableSpace.X + sizingBlock.Width - rightVal.ToPx(sizingBlock.Width) - borderBoxWidth - marginRight;
            }
            else
            {
                finalX = availableSpace.X + marginLeft;
            }

            // Вертикальное позиционирование (top имеет приоритет над bottom)
            if (topVal.Unit != Unit.Auto)
            {
                finalY = availableSpace.Y + topVal.ToPx(sizingBlock.Height);
            }
            else if (bottomVal.Unit != Unit.Auto)
            {
                // Для bottom нужно будет знать финальную высоту, пока что это предварительный расчет.
                // Финальный расчет высоты и Y-позиции для bottom: auto будет позже.
                // Здесь мы пока что не можем точно рассчитать Y.
                // Для простоты пока оставляем Y по потоку.
                finalY = availableSpace.Y + marginTop;
            }
            else
            {
                finalY = availableSpace.Y + marginTop;
            }
        }
        
        // Задаем предварительный LayoutBox. Высота пока что 0, если она 'auto'.
        float tempContentHeight = 0;
        if (height.Unit != Unit.Auto)
        {
            float borderBoxHeight = height.ToPx(sizingBlock.Height);
            tempContentHeight = Math.Max(0, borderBoxHeight - paddingTop - paddingBottom - borderTop - borderBottom);
        }
        
        parent.LayoutBox = new LayoutBox
        {
            ContentRect = new Rectangle(
                finalX + borderLeft + paddingLeft,
                finalY + borderTop + paddingTop,
                contentWidth,
                tempContentHeight
            )
        };
        
        RecalculateOuterRects(parent,
            marginLeft, marginRight, marginTop, marginBottom,
            paddingLeft, paddingRight, paddingTop, paddingBottom,
            borderLeft, borderRight, borderTop, borderBottom);

        float childrenConsumedHeight = parent.GetDisplay() switch
        {
            s.display_block => CalculateBlock(parent, font),
            s.display_flex => CalculateFlex(parent, font),
            s.display_inline_block => CalculateInlineBlock(parent, font), 
            _ => throw new Exception("Unknown display type")
        };

        if (height.Unit == Unit.Auto)
        {
            // Если высота автоматическая, она определяется максимальной высотой
            // между собственным текстом и скомпонованными дочерними элементами.
            float naturalTextHeight = CalculateNaturalSize(parent, font, contentWidth).height;
            float finalContentHeight = Math.Max(naturalTextHeight, childrenConsumedHeight);

            // Обновляем LayoutBox с финальной высотой.
            parent.LayoutBox.ContentRect = new Rectangle(
                parent.LayoutBox.ContentRect.X,
                parent.LayoutBox.ContentRect.Y,
                parent.LayoutBox.ContentRect.Width,
                finalContentHeight
            );
            RecalculateOuterRects(parent, 
                marginLeft, marginRight, marginTop, marginBottom,
                paddingLeft, paddingRight, paddingTop, paddingBottom,
                borderLeft, borderRight, borderTop, borderBottom);
        }
        
        var bottom = ParseValue(style.GetValueOrDefault(s.bottom, s.auto));
        var top = ParseValue(style.GetValueOrDefault(s.top, s.auto));
        if (position is s.position_absolute or s.position_fixed && top.Unit == Unit.Auto && bottom.Unit != Unit.Auto)
        {
            // Вычисляем новую Y-координату на основе 'bottom'.
            float newY = availableSpace.Y + sizingBlock.Height - bottom.ToPx(sizingBlock.Height) - parent.LayoutBox.MarginRect.Height;
            
            // Сдвигаем весь элемент (включая все его внутренние Rect'ы) на новую позицию.
            parent.LayoutBox.SetY(newY);
        }
    }

    private float CalculateInlineBlock(UIElement parent, Font font)
    {
        // "Каретка" для отслеживания текущей позиции
        float currentX = 0;
        float currentY = 0;
        
        // Отслеживаем максимальную высоту элементов в текущей строке
        float lineHeight = 0;

        foreach (var child in parent.Children)
        {
            // Фильтруем элементы не в потоке
            if (_absoluteElements.Contains(child) || _fixedElements.Contains(child))
            {
                continue;
            }
            
            // Сначала мы должны узнать "естественную" ширину элемента, чтобы понять,
            // поместится ли он в текущей строке. Для этого делаем предварительный расчет.
            // Мы передаем бесконечное пространство, чтобы он не был сжат.
            var preLayoutSpace = new Rectangle(0, 0, float.PositiveInfinity, float.PositiveInfinity);
            Calculate(child, preLayoutSpace, font);
            float childMarginWidth = child.LayoutBox.MarginRect.Width;
            float childMarginHeight = child.LayoutBox.MarginRect.Height;

            // Проверяем, помещается ли элемент в оставшееся место на строке
            if (currentX > 0 && (currentX + childMarginWidth > parent.LayoutBox.ContentRect.Width))
            {
                // Места нет, ПЕРЕНОС СТРОКИ
                // Сдвигаем Y вниз на высоту предыдущей строки
                currentY += lineHeight;
                // Сбрасываем X в начало новой строки
                currentX = 0;
                // Сбрасываем высоту текущей строки
                lineHeight = 0;
            }

            // Теперь, когда мы знаем финальную позицию X, мы можем сделать финальную компоновку.
            var finalAvailableSpace = new Rectangle(
                parent.LayoutBox.ContentRect.X + currentX,
                parent.LayoutBox.ContentRect.Y + currentY,
                parent.LayoutBox.ContentRect.Width - currentX, // Оставшееся место по ширине
                float.PositiveInfinity
            );
            Calculate(child, finalAvailableSpace, font);

            // Обновляем позицию каретки по X
            currentX += child.LayoutBox.MarginRect.Width;
            
            // Обновляем высоту текущей строки, если этот элемент оказался выше
            lineHeight = Math.Max(lineHeight, child.LayoutBox.MarginRect.Height);
        }

        // После завершения цикла нужно добавить высоту последней строки
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
                float.PositiveInfinity // Высота не ограничена, элемент сам определит свой размер
            );
            Calculate(child, availableSpaceForChild, font);
            
            currentY += child.LayoutBox.MarginRect.Height;
        }

        return currentY;
    }

    private float CalculateFlex(UIElement parent, Font font)
    {
        var flexItems = parent.Children.Where(c => !_absoluteElements.Contains(c)
                                                   && !_fixedElements.Contains(c)
                                                   && c.GetDisplay() != s.display_none); //TODO: вроде не нужна эта проверка
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

        List<(UIElement Element, float Basis)> itemData = [];
        foreach (var item in flexItems)
        {
            var basisVal = ParseValue(item.ComputedStyle.GetValueOrDefault(s.flex_basis, s.auto));
            float flexBasis;

            if (basisVal.Unit == Unit.Auto)
            {
                // Для 'auto', базис - это "естественный" размер элемента.
                // Чтобы его узнать, мы компонуем элемент в неограниченном пространстве по главной оси.
                var availableForSizing = new Rectangle(0, 0, 
                    isRow ? float.PositiveInfinity : contentBox.Width, // Ширина ограничена для column-flex для правильного переноса текста
                    isRow ? contentBox.Height : float.PositiveInfinity
                );
                Calculate(item, availableForSizing, font);
                flexBasis = isRow ? item.LayoutBox.MarginRect.Width : item.LayoutBox.MarginRect.Height;
            }
            else
            {
                // Если базис задан в px или %, вычисляем его от размера родительского контейнера.
                flexBasis = basisVal.ToPx(mainSize);
            }

            totalFlexBasis += flexBasis;
            itemData.Add(new ValueTuple<UIElement, float>(item, flexBasis));
        }
        
        if (ParseValue(parent.ComputedStyle.GetValueOrDefault(isRow ? s.width : s.height, s.auto)).Unit == Unit.Auto)
        {
            mainSize = totalFlexBasis;
        }
        
        float freeSpace = mainSize - totalFlexBasis;
        var finalSizes = new Dictionary<UIElement, float>();
        
        if (freeSpace > 0) // Если есть лишнее место, РАСТЯГИВАЕМ (GROW)
        {
            float totalGrow = itemData.Sum(static d => ParseFloat(d.Element.ComputedStyle.GetValueOrDefault(s.flex_grow, "0")));
            foreach (var item in itemData)
            {
                float growFactor = ParseFloat(item.Element.ComputedStyle.GetValueOrDefault(s.flex_grow, "0"));
                // Элемент получает долю свободного пространства пропорционально своему flex-grow.
                float addedSpace = (totalGrow > 0) ? (growFactor / totalGrow) * freeSpace : 0;
                finalSizes[item.Element] = item.Basis + addedSpace;
            }
        }
        else // Если места не хватает, СЖИМАЕМ (SHRINK)
        {
            float totalWeightedShrink = itemData.Sum(static d => ParseFloat(d.Element.ComputedStyle.GetValueOrDefault(s.flex_shrink, "1")) * d.Basis);
            foreach (var item in itemData)
            {
                float shrinkFactor = ParseFloat(item.Element.ComputedStyle.GetValueOrDefault(s.flex_shrink, "1"));
                // Элемент теряет долю "негативного" пространства пропорционально своему flex-shrink, взвешенному на его размер.
                float removedSpace = (totalWeightedShrink > 0) ? ((shrinkFactor * item.Basis) / totalWeightedShrink) * freeSpace : 0;
                finalSizes[item.Element] = item.Basis + removedSpace;
            }
        }
        
        // 1. Выравнивание по главной оси (Justify-Content)
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
            float crossSize = isRow ? contentBox.Height : contentBox.Width; // Полный доступный размер по поперечной оси.

            var alignItems = style.GetValueOrDefault("align-items", "stretch");
            var alignSelf = item.ComputedStyle.GetValueOrDefault(s.align_self, alignItems);

            // Определяем доступное пространство для дочернего элемента перед рекурсивным вызовом.
            Rectangle availableSpaceForChild;
            bool isStretched = alignSelf == s.align_self_stretch && 
                               (isRow ? ParseValue(item.ComputedStyle.GetValueOrDefault(s.height, s.auto)).Unit == Unit.Auto 
                                      : ParseValue(item.ComputedStyle.GetValueOrDefault(s.width, s.auto)).Unit == Unit.Auto);

            if (isStretched)
            {
                 // Для stretch передаем полный поперечный размер, чтобы элемент мог растянуться.
                 availableSpaceForChild = new Rectangle(
                    isRow ? contentBox.X + currentMainPos : contentBox.X, 
                    isRow ? contentBox.Y : contentBox.Y + currentMainPos, 
                    isRow ? finalMainSize : crossSize, 
                    isRow ? crossSize : finalMainSize);
            }
            else
            {
                 // Иначе не задаем поперечный размер (ставим 0), чтобы элемент сам определил свою высоту/ширину по контенту.
                 availableSpaceForChild = new Rectangle(
                    isRow ? contentBox.X + currentMainPos : contentBox.X, 
                    isRow ? contentBox.Y : contentBox.Y + currentMainPos, 
                    isRow ? finalMainSize : 0,
                    isRow ? 0 : finalMainSize);
            }
            
            // РЕКУРСИВНЫЙ ВЫЗОВ: компонуем дочерний элемент в выделенном для него пространстве.
            Calculate(item, availableSpaceForChild, font);
            
            // После компоновки у item есть финальные размеры. Теперь применяем выравнивание.
            float itemCrossSize = isRow ? item.LayoutBox.MarginRect.Height : item.LayoutBox.MarginRect.Width;
            maxCrossSize = Math.Max(maxCrossSize, itemCrossSize);
            
            var itemMargin = ParseAllMargins(item.ComputedStyle, parent.LayoutBox.ContentRect);
            
            // Сдвигаем элемент по поперечной оси в соответствии с align-self.
            if (alignSelf == s.align_self_flex_end)
            {
                if (isRow) item.LayoutBox.SetY(contentBox.Y + crossSize - itemCrossSize - itemMargin.Bottom);
                else item.LayoutBox.SetX(contentBox.X + crossSize - itemCrossSize - itemMargin.Right);
            }
            else if (alignSelf == s.align_self_center)
            {
                if (isRow) item.LayoutBox.SetY(contentBox.Y + (crossSize - itemCrossSize) / 2 + itemMargin.Top - itemMargin.Bottom);
                else item.LayoutBox.SetX(contentBox.X + (crossSize - itemCrossSize) / 2 + itemMargin.Left - itemMargin.Right);
            }
            
            // Сдвигаем "каретку" по главной оси для следующего элемента.
            currentMainPos += finalMainSize + spacing;
        }

        return isRow ? maxCrossSize : mainSize;
    }
    

    private (float width, float height) CalculateNaturalSize(UIElement element, Font font, float? wrapWidth = null)
    {
        var style = element.ComputedStyle;
        float textWidth = 0, textHeight = 0;
        if (!string.IsNullOrEmpty(element.Text))
        {
            var fontSize = ParseValue(style.GetValueOrDefault(s.font_size, "16")).ToPx(0);
            WrapText(element, wrapWidth ?? float.PositiveInfinity, font, fontSize);
            if (element.WrappedTextLines.Any())
            {
                textWidth = element.WrappedTextLines.Max(line =>
                    Raylib.MeasureTextEx(font, line, fontSize, 1).X);
                var lineHeight = ParseValue(style.GetValueOrDefault(s.line_height, s.auto))
                    .ToPx(fontSize, fontSize * 1.2f);
                textHeight = element.WrappedTextLines.Count * lineHeight;
            }
        }
        // Логика просмотра дочерних элементов удалена
        return (textWidth, textHeight);
    }
    
    private UIElement? FindOffsetParent(UIElement element)
    {
        var parent = element.Parent;
        while (parent != null)
        {
            var position = parent.GetPosition();
            if (position is s.position_relative or s.position_absolute or s.position_fixed) return parent;
            parent = parent.Parent;
        }

        return null;
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

    private StyleValue ParseValue(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return StyleValue.Px(0);
        value = value.Trim();
        if (value == s.auto) return StyleValue.Auto;
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
        var parts = value.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(ParseValue).ToArray();
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

    private (float Left, float Right, float Top, float Bottom) ParseAllMargins(Dictionary<string, string> style, Rectangle c)
    {
        var edges = ParseEdges(style.GetValueOrDefault(s.margin, "0"));
        return (
            edges.Left.ToPx(c.Width),
            edges.Right.ToPx(c.Width),
            edges.Top.ToPx(c.Height),
            edges.Bottom.ToPx(c.Height));
    }

    private (float Left, float Right, float Top, float Bottom) ParseAllPaddings(Dictionary<string, string> style, Rectangle c)
    {
        var edges = ParseEdges(style.GetValueOrDefault(s.padding, "0"));
        return (
            edges.Left.ToPx(c.Width),
            edges.Right.ToPx(c.Width),
            edges.Top.ToPx(c.Height),
            edges.Bottom.ToPx(c.Height));
    }

    private (float Left, float Right, float Top, float Bottom) ParseAllBorders(Dictionary<string, string> style, Rectangle c)
    {
        var edges = ParseEdges(style.GetValueOrDefault(s.border_width, "0"));
        return (
            edges.Left.ToPx(c.Width),
            edges.Right.ToPx(c.Width),
            edges.Top.ToPx(c.Height),
            edges.Bottom.ToPx(c.Height));
    }

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
}

#region Extension Classes and Structs

public static class StyleValueExtensions
{
    public static float ToPx(this StyleValue val, float baseValue, float defaultValue = 0f) => val.Unit switch
    {
        Unit.Auto => defaultValue,
        Unit.Percent when float.IsInfinity(baseValue) => defaultValue,
        Unit.Percent => (val.Value / 100f) * baseValue,
        _ => val.Value
    };
}

public static class RectangleExtensions
{
    public static Rectangle Inflate(this Rectangle rect, float left, float top, float right, float bottom) =>
        new(rect.X - left, rect.Y - top, rect.Width + left + right, rect.Height + top + bottom);
}

#endregion