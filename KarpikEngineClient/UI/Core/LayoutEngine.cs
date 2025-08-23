using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Raylib_cs;
using s = Karpik.Engine.Client.UIToolkit.StyleSheet;

namespace Karpik.Engine.Client.UIToolkit
{
    public class LayoutEngine
    {
        private readonly List<UIElement> _absoluteElements = [];
        private readonly List<UIElement> _fixedElements = [];
        private Rectangle _viewport;

        // Вспомогательные классы для Flexbox
        private class FlexItemData
        {
            public UIElement Element { get; init; }
            public float FlexBasis { get; set; }
            public float FinalMainSize { get; set; }
        }

        private class FlexLine
        {
            public List<FlexItemData> Items { get; init; } = new();
            public float TotalFlexBasis { get; set; }
            public float MainSize { get; set; }
            public float CrossSize { get; set; }
        }

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
    parent.LayoutBox = new LayoutBox();

    if (parent.ComputedStyle.GetValueOrDefault("display") == "none")
    {
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
    var paddingLeft = ParseValue(style.GetValueOrDefault(s.padding_left, "0")).ToPx(sizingBlock.Width); // padding % зависит от ширины родителя
    var paddingRight = ParseValue(style.GetValueOrDefault(s.padding_right, "0")).ToPx(sizingBlock.Width);

    var marginTop = ParseValue(style.GetValueOrDefault(s.margin_top, "0")).ToPx(sizingBlock.Height);
    var marginBottom = ParseValue(style.GetValueOrDefault(s.margin_bottom, "0")).ToPx(sizingBlock.Height);
    var borderTop = ParseValue(style.GetValueOrDefault(s.border_top_width, "0")).ToPx(0);
    var borderBottom = ParseValue(style.GetValueOrDefault(s.border_bottom_width, "0")).ToPx(0);
    var paddingTop = ParseValue(style.GetValueOrDefault(s.padding_top, "0")).ToPx(sizingBlock.Height); // padding % зависит от высоты родителя
    var paddingBottom = ParseValue(style.GetValueOrDefault(s.padding_bottom, "0")).ToPx(sizingBlock.Height);
    
    // --- НАЧАЛО ИЗМЕНЕНИЙ: box-sizing ---
    
    var boxSizing = style.GetValueOrDefault(s.box_sizing, "content-box");
    var widthVal = ParseValue(style.GetValueOrDefault(s.width, s.auto));
    float contentWidth;

    if (widthVal.Unit != Unit.Auto)
    {
        float explicitWidth = widthVal.ToPx(sizingBlock.Width);
        if (boxSizing == s.box_sizing_border_box)
        {
            contentWidth = Math.Max(0, explicitWidth - paddingLeft - paddingRight - borderLeft - borderRight);
        }
        else // content-box
        {
            contentWidth = explicitWidth;
        }
    }
    else // width: auto
    {
        if (float.IsInfinity(availableSpace.Width))
        {
            // Shrink-to-fit: content width is determined by the content itself.
            var (naturalWidth, _) = CalculateNaturalSize(parent, font, float.PositiveInfinity);
            contentWidth = naturalWidth;
        }
        else
        {
            // Fill available space. Calculate the total available width for the border-box...
            float borderBoxWidth = availableSpace.Width - marginLeft - marginRight;
            // ...and then determine the content width from that.
            contentWidth = Math.Max(0, borderBoxWidth - paddingLeft - paddingRight - borderLeft - borderRight);
        }
    }
    
    var fontSize = ParseValue(style.GetValueOrDefault(s.font_size, "16")).ToPx(0);
    WrapText(parent, contentWidth, font, fontSize);

    var heightVal = ParseValue(style.GetValueOrDefault(s.height, s.auto));
    float finalContentHeight;

    if (heightVal.Unit != Unit.Auto)
    {
        float explicitHeight = heightVal.ToPx(sizingBlock.Height);
        if (boxSizing == s.box_sizing_border_box)
        {
            finalContentHeight = Math.Max(0, explicitHeight - paddingTop - paddingBottom - borderTop - borderBottom);
        }
        else // content-box
        {
            finalContentHeight = explicitHeight;
        }
    }
    else
    {
        finalContentHeight = 0; // Будет вычислена после дочерних элементов
    }
    
    // --- КОНЕЦ ИЗМЕНЕНИЙ ---
    
    float finalX, finalY;
    if (position == s.position_static)
    {
        finalX = availableSpace.X + marginLeft;
        finalY = availableSpace.Y + marginTop;
    }
    else if (position == s.position_relative)
    {
        finalX = availableSpace.X + marginLeft;
        finalY = availableSpace.Y + marginTop;

        var leftVal = ParseValue(style.GetValueOrDefault(s.left, s.auto));
        var rightVal = ParseValue(style.GetValueOrDefault(s.right, s.auto));
        var topVal = ParseValue(style.GetValueOrDefault(s.top, s.auto));
        var bottomVal = ParseValue(style.GetValueOrDefault(s.bottom, s.auto));
        
        if (leftVal.Unit != Unit.Auto) finalX += leftVal.ToPx(sizingBlock.Width);
        else if (rightVal.Unit != Unit.Auto) finalX -= rightVal.ToPx(sizingBlock.Width);

        if (topVal.Unit != Unit.Auto) finalY += topVal.ToPx(sizingBlock.Height);
        else if (bottomVal.Unit != Unit.Auto) finalY -= bottomVal.ToPx(sizingBlock.Height);
    }
    else // absolute or fixed
    {
        var leftVal = ParseValue(style.GetValueOrDefault(s.left, s.auto));
        var rightVal = ParseValue(style.GetValueOrDefault(s.right, s.auto));
        var topVal = ParseValue(style.GetValueOrDefault(s.top, s.auto));
        var bottomVal = ParseValue(style.GetValueOrDefault(s.bottom, s.auto));

        float borderBoxWidth = contentWidth + paddingLeft + paddingRight + borderLeft + borderRight;
        if (leftVal.Unit != Unit.Auto) finalX = availableSpace.X + leftVal.ToPx(sizingBlock.Width);
        else if (rightVal.Unit != Unit.Auto) finalX = availableSpace.X + sizingBlock.Width - rightVal.ToPx(sizingBlock.Width) - borderBoxWidth - marginRight;
        else finalX = availableSpace.X + marginLeft;
        
        if (topVal.Unit != Unit.Auto) finalY = availableSpace.Y + topVal.ToPx(sizingBlock.Height);
        else finalY = availableSpace.Y + marginTop;
    }

    parent.LayoutBox.ContentRect = new Rectangle(finalX + borderLeft + paddingLeft, finalY + borderTop + paddingTop, contentWidth, finalContentHeight);
    RecalculateOuterRects(parent, marginLeft, marginRight, marginTop, marginBottom, paddingLeft, paddingRight, paddingTop, paddingBottom, borderLeft, borderRight, borderTop, borderBottom);
    
    float childrenConsumedHeight = LayoutChildren(parent, parent.LayoutBox.ContentRect, font);
    
    if (heightVal.Unit == Unit.Auto)
    {
        float naturalTextHeight = 0;
        if (parent.WrappedTextLines.Any())
        {
            var currentFontSize = ParseValue(style.GetValueOrDefault(s.font_size, "16")).ToPx(0);
            var lineHeight = ParseValue(style.GetValueOrDefault(s.line_height, "auto")).ToPx(currentFontSize, currentFontSize * 1.2f);
            naturalTextHeight = parent.WrappedTextLines.Count * lineHeight;
        }

        finalContentHeight = Math.Max(naturalTextHeight, childrenConsumedHeight);
        parent.LayoutBox.ContentRect = new Rectangle(parent.LayoutBox.ContentRect.X, parent.LayoutBox.ContentRect.Y, contentWidth, finalContentHeight);
        RecalculateOuterRects(parent, marginLeft, marginRight, marginTop, marginBottom, paddingLeft, paddingRight, paddingTop, paddingBottom, borderLeft, borderRight, borderTop, borderBottom);
    }
    
    var bottomProp = ParseValue(style.GetValueOrDefault(s.bottom, s.auto));
    var topProp = ParseValue(style.GetValueOrDefault(s.top, s.auto));
    if (position is s.position_absolute or s.position_fixed && topProp.Unit == Unit.Auto && bottomProp.Unit != Unit.Auto)
    {
        float newY = availableSpace.Y + sizingBlock.Height - bottomProp.ToPx(sizingBlock.Height) - parent.LayoutBox.MarginRect.Height;
        parent.LayoutBox.SetY(newY);
    }
}

        private float LayoutChildren(UIElement parent, Rectangle parentAvailableSpace, Font font)
        {
            return parent.GetDisplay() switch
            {
                s.display_block => CalculateBlock(parent, font),
                s.display_flex => CalculateFlex(parent, parentAvailableSpace, font),
                _ => 0
            };
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
                
                currentY += child.LayoutBox.MarginRect.Height;
            }
            return currentY;
        }

        private float CalculateFlex(UIElement parent, Rectangle parentAvailableSpace, Font font)
        {
            var flexItems = parent.Children.Where(c => !_absoluteElements.Contains(c) 
                                                       && !_fixedElements.Contains(c) 
                                                       && c.GetDisplay() != s.display_none);
            if (!flexItems.Any()) return 0;

            var style = parent.ComputedStyle;
            var contentBox = parent.LayoutBox.ContentRect;
            bool isRow = parent.IsRow();

            // --- ФИНАЛЬНОЕ ИСПРАВЛЕНИЕ ---
            // Вместо того, чтобы читать `mainSize` из потенциально недостроенного `contentBox`,
            // мы используем `parentAvailableSpace`, который является надежным источником данных.
            float mainSize;
            if (isRow)
            {
                mainSize = contentBox.Width; // Ширина всегда вычисляется до дочерних элементов.
            }
            else
            {
                // Для колонок `mainSize` - это высота. Если она auto, мы должны использовать
                // доступную высоту от родителя, а не свою текущую (которая равна 0).
                var heightVal = ParseValue(style.GetValueOrDefault(s.height, s.auto));
                mainSize = heightVal.Unit == Unit.Auto ? parentAvailableSpace.Height : contentBox.Height;
                if (float.IsInfinity(mainSize)) mainSize = 0;
            }
            // --- КОНЕЦ ИСПРАВЛЕНИЯ ---
            
            var allItemData = flexItems.Select(item =>
            {
                var basisVal = ParseValue(item.ComputedStyle.GetValueOrDefault(s.flex_basis, s.auto));
                float flexBasis;
                if (basisVal.Unit == Unit.Auto)
                {
                    // --- НАЧАЛО ИСПРАВЛЕНИЯ ---
                    // Раньше здесь был вызов CalculateNaturalSize, который не умел работать с контейнерами.
                    // Теперь мы делаем полноценный, но временный, расчет компоновки,
                    // чтобы получить реальный внутренний размер элемента с учетом его детей.
                    var availableSpaceForMeasure = new Rectangle(
                        0, 0,
                        isRow ? float.PositiveInfinity : contentBox.Width, // Неограниченная ширина для строки
                        isRow ? contentBox.Height : float.PositiveInfinity // Неограниченная высота для колонки
                    );
                    Calculate(item, availableSpaceForMeasure, font);

                    // flex-basis - это размер margin-box элемента.
                    flexBasis = isRow ? item.LayoutBox.MarginRect.Width : item.LayoutBox.MarginRect.Height;
                    // --- КОНЕЦ ИСПРАВЛЕНИЯ ---
                }
                else
                {
                    flexBasis = basisVal.ToPx(mainSize);
                }
                return new FlexItemData { Element = item, FlexBasis = flexBasis };
            }).ToList();

            var flexWrap = style.GetValueOrDefault(s.flex_wrap, s.flex_wrap_nowrap);
            var allLines = new List<FlexLine>();

            if (flexWrap == s.flex_wrap_nowrap)
            {
                allLines.Add(new FlexLine { Items = allItemData });
            }
            else
            {
                var currentLine = new FlexLine();
                foreach (var item in allItemData)
                {
                    if (currentLine.Items.Any() && currentLine.TotalFlexBasis + item.FlexBasis > mainSize && mainSize > 0)
                    {
                        allLines.Add(currentLine);
                        currentLine = new FlexLine();
                    }
                    currentLine.Items.Add(item);
                    currentLine.TotalFlexBasis += item.FlexBasis;
                }
                if (currentLine.Items.Any()) allLines.Add(currentLine);
            }

            foreach (var line in allLines)
            {
                line.TotalFlexBasis = line.Items.Sum(i => i.FlexBasis);
                float freeSpace = mainSize - line.TotalFlexBasis;

                if (freeSpace > 0)
                {
                    float totalGrow = line.Items.Sum(d => ParseFloat(d.Element.ComputedStyle.GetValueOrDefault(s.flex_grow, "0")));
                    foreach (var item in line.Items)
                    {
                        float growFactor = ParseFloat(item.Element.ComputedStyle.GetValueOrDefault(s.flex_grow, "0"));
                        item.FinalMainSize = item.FlexBasis + (totalGrow > 0 ? (growFactor / totalGrow) * freeSpace : 0);
                    }
                }
                else
                {
                    float totalWeightedShrink = line.Items.Sum(d => ParseFloat(d.Element.ComputedStyle.GetValueOrDefault(s.flex_shrink, "1")) * d.FlexBasis);
                    foreach (var item in line.Items)
                    {
                        float shrinkFactor = ParseFloat(item.Element.ComputedStyle.GetValueOrDefault(s.flex_shrink, "1"));
                        item.FinalMainSize = item.FlexBasis + (totalWeightedShrink > 0 ? ((shrinkFactor * item.FlexBasis) / totalWeightedShrink) * freeSpace : 0);
                    }
                }
                line.MainSize = line.Items.Sum(i => i.FinalMainSize);
            }
            
            foreach (var line in allLines)
            {
                float lineCrossSize = 0;
                foreach (var item in line.Items)
                {
                    var tempAvailableSpace = new Rectangle(0, 0, isRow ? item.FinalMainSize : contentBox.Width, isRow ? mainSize : item.FinalMainSize);
                    Calculate(item.Element, tempAvailableSpace, font);
                    float itemCrossSize = isRow ? item.Element.LayoutBox.MarginRect.Height : item.Element.LayoutBox.MarginRect.Width;
                    lineCrossSize = Math.Max(lineCrossSize, itemCrossSize);
                }
                line.CrossSize = lineCrossSize;
            }

            float crossSizeForSizing = isRow ? contentBox.Height : contentBox.Width;
            if (float.IsInfinity(crossSizeForSizing)) crossSizeForSizing = 0;

            var lineStartOffsets = new List<float>();
            float totalCrossSize = allLines.Sum(l => l.CrossSize);
            float freeCrossSpace = crossSizeForSizing - totalCrossSize;
            if (freeCrossSpace > 0.01f && allLines.Any())
            {
                float crossOffset = 0, lineSpacing = 0;
                var alignContent = style.GetValueOrDefault(s.align_content, s.align_content_stretch);
                switch (alignContent)
                {
                    case s.align_content_flex_end: crossOffset = freeCrossSpace; break;
                    case s.align_content_center: crossOffset = freeCrossSpace / 2; break;
                    case s.align_content_space_between: if(allLines.Count > 1) lineSpacing = freeCrossSpace / (allLines.Count - 1); break;
                    case s.align_content_space_around: if (allLines.Count > 0) { lineSpacing = freeCrossSpace / allLines.Count; crossOffset = lineSpacing / 2; } break;
                    case s.align_content_stretch: if (allLines.Count > 0) { foreach (var l in allLines) l.CrossSize += freeCrossSpace / allLines.Count; } break;
                }
                
                float runningPos = crossOffset;
                foreach (var line in allLines) { lineStartOffsets.Add(runningPos); runningPos += line.CrossSize + lineSpacing; }
            }
            else
            {
                float runningPos = 0;
                foreach (var line in allLines) { lineStartOffsets.Add(runningPos); runningPos += line.CrossSize; }
            }

            for (int i = 0; i < allLines.Count; i++)
            {
                var line = allLines[i];
                float lineStartOffset = lineStartOffsets[i];
                
                var justifyContent = parent.GetJustifyContent();
                float mainAxisOffset = 0, spacing = 0;
                float finalFreeSpace = mainSize - line.MainSize;
                if (finalFreeSpace > 0.01f)
                {
                    var count = line.Items.Count;
                    switch (justifyContent)
                    {
                        case s.justify_content_flex_end: mainAxisOffset = finalFreeSpace; break;
                        case s.justify_content_center: mainAxisOffset = finalFreeSpace / 2; break;
                        case s.justify_content_space_between: if (count > 1) spacing = finalFreeSpace / (count - 1); break;
                        case s.justify_content_space_around: if (count > 0) { spacing = finalFreeSpace / count; mainAxisOffset = spacing / 2; } break;
                    }
                }
                
                float mainAxisPosition = mainAxisOffset;
                foreach (var item in line.Items)
                {
                    var itemElement = item.Element;
                    var itemStyle = itemElement.ComputedStyle;
                    var alignSelf = itemStyle.GetValueOrDefault(s.align_self, style.GetValueOrDefault(s.align_items, s.align_stretch));

                    // --- НОВАЯ ЛОГИКА РАСТЯГИВАНИЯ (STRETCH) ---
                    if (alignSelf == s.align_stretch)
                    {
                        if (isRow) // Для строки растягиваем высоту
                        {
                            var heightVal = ParseValue(itemStyle.GetValueOrDefault(s.height, s.auto));
                            if (heightVal.Unit == Unit.Auto)
                            {
                                var mTop = ParseValue(itemStyle.GetValueOrDefault(s.margin_top, "0")).ToPx(crossSizeForSizing);
                                var mBottom = ParseValue(itemStyle.GetValueOrDefault(s.margin_bottom, "0")).ToPx(crossSizeForSizing);
                                var bTop = ParseValue(itemStyle.GetValueOrDefault(s.border_top_width, "0")).ToPx(0);
                                var bBottom = ParseValue(itemStyle.GetValueOrDefault(s.border_bottom_width, "0")).ToPx(0);
                                var pTop = ParseValue(itemStyle.GetValueOrDefault(s.padding_top, "0")).ToPx(0);
                                var pBottom = ParseValue(itemStyle.GetValueOrDefault(s.padding_bottom, "0")).ToPx(0);
                                
                                float newBorderBoxHeight = line.CrossSize - mTop - mBottom;
                                float newContentHeight = Math.Max(0, newBorderBoxHeight - bTop - bBottom - pTop - pBottom);
                                
                                itemElement.LayoutBox.ContentRect = new Rectangle(itemElement.LayoutBox.ContentRect.X, itemElement.LayoutBox.ContentRect.Y, itemElement.LayoutBox.ContentRect.Width, newContentHeight);
                                
                                var mLeft = ParseValue(itemStyle.GetValueOrDefault(s.margin_left, "0")).ToPx(mainSize);
                                var mRight = ParseValue(itemStyle.GetValueOrDefault(s.margin_right, "0")).ToPx(mainSize);
                                var bLeft = ParseValue(itemStyle.GetValueOrDefault(s.border_left_width, "0")).ToPx(0);
                                var bRight = ParseValue(itemStyle.GetValueOrDefault(s.border_right_width, "0")).ToPx(0);
                                var pLeft = ParseValue(itemStyle.GetValueOrDefault(s.padding_left, "0")).ToPx(0);
                                var pRight = ParseValue(itemStyle.GetValueOrDefault(s.padding_right, "0")).ToPx(0);

                                RecalculateOuterRects(itemElement, mLeft, mRight, mTop, mBottom, pLeft, pRight, pTop, pBottom, bLeft, bRight, bTop, bBottom);
                            }
                        }
                        else // Для колонки растягиваем ширину
                        {
                            var widthVal = ParseValue(itemStyle.GetValueOrDefault(s.width, s.auto));
                            if (widthVal.Unit == Unit.Auto)
                            {
                                var mLeft = ParseValue(itemStyle.GetValueOrDefault(s.margin_left, "0")).ToPx(crossSizeForSizing);
                                var mRight = ParseValue(itemStyle.GetValueOrDefault(s.margin_right, "0")).ToPx(crossSizeForSizing);
                                var bLeft = ParseValue(itemStyle.GetValueOrDefault(s.border_left_width, "0")).ToPx(0);
                                var bRight = ParseValue(itemStyle.GetValueOrDefault(s.border_right_width, "0")).ToPx(0);
                                var pLeft = ParseValue(itemStyle.GetValueOrDefault(s.padding_left, "0")).ToPx(0);
                                var pRight = ParseValue(itemStyle.GetValueOrDefault(s.padding_right, "0")).ToPx(0);

                                float newBorderBoxWidth = line.CrossSize - mLeft - mRight;
                                float newContentWidth = Math.Max(0, newBorderBoxWidth - bLeft - bRight - pLeft - pRight);
                                
                                itemElement.LayoutBox.ContentRect = new Rectangle(itemElement.LayoutBox.ContentRect.X, itemElement.LayoutBox.ContentRect.Y, newContentWidth, itemElement.LayoutBox.ContentRect.Height);
                                
                                var mTop = ParseValue(itemStyle.GetValueOrDefault(s.margin_top, "0")).ToPx(mainSize);
                                var mBottom = ParseValue(itemStyle.GetValueOrDefault(s.margin_bottom, "0")).ToPx(mainSize);
                                var bTop = ParseValue(itemStyle.GetValueOrDefault(s.border_top_width, "0")).ToPx(0);
                                var bBottom = ParseValue(itemStyle.GetValueOrDefault(s.border_bottom_width, "0")).ToPx(0);
                                var pTop = ParseValue(itemStyle.GetValueOrDefault(s.padding_top, "0")).ToPx(0);
                                var pBottom = ParseValue(itemStyle.GetValueOrDefault(s.padding_bottom, "0")).ToPx(0);

                                RecalculateOuterRects(itemElement, mLeft, mRight, mTop, mBottom, pLeft, pRight, pTop, pBottom, bLeft, bRight, bTop, bBottom);
                            }
                        }
                    }
                    
                    // --- ЛОГИКА ПОЗИЦИОНИРОВАНИЯ ---
                    // Главная ось
                    if (isRow) itemElement.LayoutBox.SetX(contentBox.X + mainAxisPosition);
                    else itemElement.LayoutBox.SetY(contentBox.Y + mainAxisPosition);
                    
                    // Поперечная ось
                    float itemCrossSize = isRow ? itemElement.LayoutBox.MarginRect.Height : itemElement.LayoutBox.MarginRect.Width;
                    float crossPos = lineStartOffset; // По умолчанию flex-start (или stretch)
                    
                    if (alignSelf == s.align_flex_end) crossPos = lineStartOffset + line.CrossSize - itemCrossSize;
                    else if (alignSelf == s.align_center) crossPos = lineStartOffset + (line.CrossSize - itemCrossSize) / 2;
                    
                    if (isRow) itemElement.LayoutBox.SetY(contentBox.Y + crossPos);
                    else itemElement.LayoutBox.SetX(contentBox.X + crossPos);

                    // --- РЕКУРСИВНАЯ КОМПОНОВКА ДОЧЕРНИХ ЭЛЕМЕНТОВ ---
                    LayoutChildren(itemElement, itemElement.LayoutBox.ContentRect, font);

                    mainAxisPosition += item.FinalMainSize + spacing;
                }
            }

            return allLines.Sum(l => l.CrossSize);
        }

        private (float width, float height) CalculateNaturalSize(UIElement element, Font defaultFont,
            float? wrapWidth = null)
        {
            var style = element.ComputedStyle;
            var fontSize = ParseValue(style.GetValueOrDefault(s.font_size, "16")).ToPx(0);

            // 1. Рассчитываем размер собственного текста элемента
            // Используем предоставленную ширину для переноса, если она есть
            WrapText(element, wrapWidth ?? float.PositiveInfinity, defaultFont, fontSize);

            float textWidth = 0, textHeight = 0;
            if (element.WrappedTextLines.Any())
            {
                textWidth = element.WrappedTextLines.Max(line =>
                    Raylib.MeasureTextEx(defaultFont, line, fontSize, 1).X);
                var lineHeight = ParseValue(style.GetValueOrDefault(s.line_height, "auto"))
                    .ToPx(fontSize, fontSize * 1.2f);
                textHeight = element.WrappedTextLines.Count * lineHeight;
            }

            // 2. Рассчитываем совокупный размер дочерних элементов в потоке
            float childrenWidth = 0, childrenHeight = 0;
            var flowChildren = element.Children
                .Where(c => c.GetPosition() == s.position_static || c.GetPosition() == s.position_relative).ToList();

            if (flowChildren.Any())
            {
                // Рекурсивно получаем естественные размеры всех дочерних элементов
                var childSizes = flowChildren.Select(c =>
                    CalculateNaturalSize(c, defaultFont,
                        // Если родитель - flex-колонка, передаем ему нашу `wrapWidth`
                        element.GetDisplay() == "flex" && !element.IsRow() ? wrapWidth : null)
                );

                if (element.GetDisplay() == s.display_flex)
                {
                    if (element.IsRow())
                    {
                        // Для flex-строки естественная ширина - это сумма ширин детей,
                        // а естественная высота - высота самого высокого ребенка.
                        childrenWidth = childSizes.Sum(s => s.width);
                        childrenHeight = childSizes.Any() ? childSizes.Max(s => s.height) : 0;
                    }
                    else // flex-колонка
                    {
                        // Для flex-колонки естественная ширина - это ширина самого широкого ребенка,
                        // а естественная высота - сумма высот детей.
                        childrenWidth = childSizes.Any() ? childSizes.Max(s => s.width) : 0;
                        childrenHeight = childSizes.Sum(s => s.height);
                    }
                }
                else // display: block (или другой, не flex)
                {
                    // Для block-контейнера естественная ширина - это ширина самого широкого ребенка,
                    // а естественная высота - сумма высот детей.
                    childrenWidth = childSizes.Any() ? childSizes.Max(s => s.width) : 0;
                    childrenHeight = childSizes.Sum(s => s.height);
                }
            }

            // 3. Финальный естественный размер - это максимум из размера текста и размера дочерних элементов
            return (Math.Max(textWidth, childrenWidth), Math.Max(textHeight, childrenHeight));
        }

        private void RecalculateOuterRects(UIElement element, 
            float mLeft, float mRight, float mTop, float mBottom, 
            float pLeft, float pRight, float pTop, float pBottom, 
            float bLeft, float bRight, float bTop, float bBottom)
        {
            var contentRect = element.LayoutBox.ContentRect;
            element.LayoutBox.PaddingRect = new Rectangle(contentRect.X - pLeft, contentRect.Y - pTop, contentRect.Width + pLeft + pRight, contentRect.Height + pTop + pBottom);
            element.LayoutBox.BorderRect = new Rectangle(element.LayoutBox.PaddingRect.X - bLeft, element.LayoutBox.PaddingRect.Y - bTop, element.LayoutBox.PaddingRect.Width + bLeft + bRight, element.LayoutBox.PaddingRect.Height + bTop + bBottom);
            element.LayoutBox.MarginRect = new Rectangle(element.LayoutBox.BorderRect.X - mLeft, element.LayoutBox.BorderRect.Y - mTop, element.LayoutBox.BorderRect.Width + mLeft + mRight, element.LayoutBox.BorderRect.Height + mTop + mBottom);
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
            if (value.EndsWith("px")) { if (float.TryParse(value.AsSpan(0, value.Length - 2), NumberStyles.Any, CultureInfo.InvariantCulture, out float pxVal)) return StyleValue.Px(pxVal); }
            if (value.EndsWith("%")) { if (float.TryParse(value.AsSpan(0, value.Length - 1), NumberStyles.Any, CultureInfo.InvariantCulture, out float percentVal)) return StyleValue.Percent(percentVal); }
            if (float.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out float numVal)) return StyleValue.Px(numVal);
            return StyleValue.Px(0);
        }
        
        private static float ParseFloat(string value, float defaultValue = 0f)
        {
            if (string.IsNullOrWhiteSpace(value)) return defaultValue;
            value = value.Replace("px", "").Trim();
            return float.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out float result) ? result : defaultValue;
        }

        private void WrapText(UIElement e, float maxWidth, Font font, float fontSize)
        {
            e.WrappedTextLines.Clear();
            if (string.IsNullOrEmpty(e.Text)) return;
            if (maxWidth <= 1) { e.WrappedTextLines.Add(e.Text); return; }
            
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

    public static class StyleValueExtensions
    {
        public static float ToPx(this StyleValue val, float baseValue, float defaultValue = 0f) => val.Unit switch
        {
            Unit.Auto => defaultValue,
            Unit.Percent when float.IsInfinity(baseValue) || float.IsNaN(baseValue) => defaultValue,
            Unit.Percent => (val.Value / 100f) * baseValue,
            _ => val.Value
        };
    }
}