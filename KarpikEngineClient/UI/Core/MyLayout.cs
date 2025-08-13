using System.Numerics;
using Raylib_cs;

namespace Karpik.Engine.Client.UIToolkit;

public static class MyLayout
{
    public static void Calculate(VisualElement root, StyleSheet globalStyleSheet, Rectangle availableSpace)
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
        
        // Пропускаем элементы, которые игнорируют layout (например, во время анимации)
        if (element.IgnoreLayout) 
        {
            return;
        }
        
        var computedStyle = element.ComputeStyle();
        
        element.ResolvedStyle.CopyFrom(computedStyle);
        
        // 1. Рассчитываем размеры элемента
        CalculateElementSize(element, computedStyle, availableSpace);
        
        // 2. Рассчитываем позицию для абсолютно и фиксированно позиционированных элементов
        if (computedStyle.GetPositionOrDefault() == Position.Absolute)
        {
            CalculateAbsolutePosition(element, computedStyle, availableSpace);
        }
        else if (computedStyle.GetPositionOrDefault() == Position.Fixed)
        {
            CalculateFixedPosition(element, computedStyle);
        }

        foreach (var child in element.Children.Where(static x => x.Visible))
        {
            CalculateLayoutRecursive(child, new Rectangle(
                element.Position.X + element.Style.Padding.Left,
                element.Position.Y + element.Style.Padding.Top,
                element.Size.X - element.Style.Padding.Left - element.Style.Padding.Right,
                element.Size.Y - element.Style.Padding.Top - element.Style.Padding.Bottom));
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
            size.Y = CalculateIntrinsicHeight(element, style);
        }
        
        if (style.MinWidth.HasValue) size.X = Math.Max(size.X, style.MinWidth.Value);
        if (style.MaxWidth.HasValue) size.X = Math.Min(size.X, style.MaxWidth.Value);
        if (style.MinHeight.HasValue) size.Y = Math.Max(size.Y, style.MinHeight.Value);
        if (style.MaxHeight.HasValue) size.Y = Math.Min(size.Y, style.MaxHeight.Value);
        
        element.Size = size;
    }
    
    private static float CalculateIntrinsicWidth(VisualElement element, Style style)
    {
        // Если у элемента уже есть размер, используем его
        if (element.Size.X > 0)
        {
            return element.Size.X;
        }
        
        float calculatedWidth = 0;
        var padding = style.Padding.Left + style.Padding.Right;
        
        if (element is ITextProvider textProvider)
        {
            var displayText = textProvider.GetDisplayText();
            if (!string.IsNullOrEmpty(displayText))
            {
                var textWidth = Raylib.MeasureText(displayText, style.GetFontSizeOrDefault());
                calculatedWidth = Math.Max(calculatedWidth, textWidth);
            }
            
            var placeholderText = textProvider.GetPlaceholderText();
            if (!string.IsNullOrEmpty(placeholderText))
            {
                var placeholderWidth = Raylib.MeasureText(placeholderText, style.GetFontSizeOrDefault());
                calculatedWidth = Math.Max(calculatedWidth, placeholderWidth);
            }
            
            var textOptions = textProvider.GetTextOptions();
            if (textOptions != null)
            {
                foreach (var option in textOptions)
                {
                    if (!string.IsNullOrEmpty(option))
                    {
                        var optionWidth = Raylib.MeasureText(option, style.GetFontSizeOrDefault());
                        calculatedWidth = Math.Max(calculatedWidth, optionWidth);
                    }
                }
                
                calculatedWidth += 30;
            }
        }
        
        calculatedWidth += padding;
        
        var minimumWidth = style.GetMinWidthOrDefault();
        calculatedWidth = Math.Max(calculatedWidth, minimumWidth);
        return calculatedWidth;
    }
    
    private static float CalculateIntrinsicHeight(VisualElement element, Style style)
    {
        // Если у элемента уже есть размер, используем его
        if (element.Size.Y > 0)
        {
            return element.Size.Y;
        }
        
        var padding = style.Padding.Top + style.Padding.Bottom;
        var calculatedHeight = style.GetFontSizeOrDefault() + padding;
        
        if (element is ITextProvider textProvider)
        {
            var displayText = textProvider.GetDisplayText();
            if (!string.IsNullOrEmpty(displayText))
            {
                calculatedHeight += 10;
            }
        }
        
        var minimumHeight = style.GetMinHeightOrDefault();
        calculatedHeight = Math.Max(calculatedHeight, minimumHeight);
        return calculatedHeight;
    }
    
    private static void CalculateAbsolutePosition(VisualElement element, Style style, Rectangle availableSpace)
    {
        var position = element.Position;
        
        if (style.Left.HasValue) position.X = availableSpace.X + style.Left.Value;
        else if (style.Right.HasValue) position.X = availableSpace.X + availableSpace.Width - element.Size.X - style.Right.Value;
            
        if (style.Top.HasValue) position.Y = availableSpace.Y + style.Top.Value;
        else if (style.Bottom.HasValue) position.Y = availableSpace.Y + availableSpace.Height - element.Size.Y - style.Bottom.Value;
        
        element.Position = position;
    }
    
    private static void CalculateFixedPosition(VisualElement element, Style style)
    {
        var position = element.Position;
        
        // Fixed позиционирование относительно viewport (экрана)
        if (style.Left.HasValue) position.X = style.Left.Value;
        else if (style.Right.HasValue) position.X = Raylib.GetScreenWidth() - element.Size.X - style.Right.Value;
            
        if (style.Top.HasValue) position.Y = style.Top.Value;
        else if (style.Bottom.HasValue) position.Y = Raylib.GetScreenHeight() - element.Size.Y - style.Bottom.Value;
        
        element.Position = position;
    }
}