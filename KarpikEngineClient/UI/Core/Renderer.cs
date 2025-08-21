using Raylib_cs;
using System.Numerics;
using Color = Raylib_cs.Color;
using s = Karpik.Engine.Client.UIToolkit.StyleSheet;

namespace Karpik.Engine.Client.UIToolkit;

public class StackingContext
{
    public UIElement RootElement { get; }
    public int ZIndex { get; }
    public List<StackingContext> ChildrenWithNegativeZIndex { get; } = new();
    public List<UIElement> ChildrenInFlow { get; } = new();
    public List<StackingContext> ChildrenWithPositiveZIndex { get; } = new();

    public StackingContext(UIElement rootElement, int zIndex = 0)
    {
        RootElement = rootElement;
        ZIndex = zIndex;
    }
}

public class Renderer
{
    public void Render(UIElement root, Font font)
    {
        var rootContext = BuildStackingContextTree(root);
        RenderContext(rootContext, font);
    }
    
    private StackingContext BuildStackingContextTree(UIElement element)
    {
        var zIndex = IsStackingContext(element) 
            ? ParseInt(element.ComputedStyle.GetValueOrDefault(s.z_index, "0")) 
            : 0;

        var context = new StackingContext(element, zIndex);

        foreach (var child in element.Children)
        {
            if (IsStackingContext(child))
            {
                var childContext = BuildStackingContextTree(child);
                if (childContext.ZIndex < 0) context.ChildrenWithNegativeZIndex.Add(childContext);
                else context.ChildrenWithPositiveZIndex.Add(childContext);
            }
            else context.ChildrenInFlow.Add(child);
        }

        context.ChildrenWithNegativeZIndex.Sort((a, b) => a.ZIndex.CompareTo(b.ZIndex));
        context.ChildrenWithPositiveZIndex.Sort((a, b) => a.ZIndex.CompareTo(b.ZIndex));

        return context;
    }
    
    private void RenderContext(StackingContext context, Font font)
    {
        var element = context.RootElement;
        if (element.ComputedStyle.GetValueOrDefault(s.display) == s.display_none) return;

        RenderElementVisuals(element, font);
        
        foreach (var childContext in context.ChildrenWithNegativeZIndex) RenderContext(childContext, font);
        foreach (var childElement in context.ChildrenInFlow) RenderContext(BuildStackingContextTree(childElement), font);
        foreach (var childContext in context.ChildrenWithPositiveZIndex) RenderContext(childContext, font);
    }
    
    private void RenderElementVisuals(UIElement element, Font font)
    {
        var style = element.ComputedStyle;
        var box = element.LayoutBox;
    
        bool hasOverflowHidden = style.GetValueOrDefault("overflow") == "hidden";
        if (hasOverflowHidden) Raylib.BeginScissorMode((int)box.PaddingRect.X, (int)box.PaddingRect.Y, (int)box.PaddingRect.Width, (int)box.PaddingRect.Height);

        var bgColor = ParseColor(style.GetValueOrDefault(s.background_color, s.transparent));
        if (bgColor.A > 0) Raylib.DrawRectangleRec(box.PaddingRect, bgColor);

        var borderColor = ParseColor(style.GetValueOrDefault(s.border_color, "black"));
        if (borderColor.A > 0)
        {
            var borderWidth = ParseFloat(style.GetValueOrDefault(s.border_width, "0"));
            if (borderWidth > 0)
                Raylib.DrawRectangleLinesEx(box.BorderRect, borderWidth, borderColor);
        }
        
        if (element.WrappedTextLines.Any())
        {
            var textColor = ParseColor(style.GetValueOrDefault(s.color, "black"));
            var fontSize = ParseFloat(style.GetValueOrDefault(s.font_size, "16"));
            var lineHeight = ParseFloat(style.GetValueOrDefault(s.line_height, "auto"), fontSize * 1.2f);
            var textAlign = style.GetValueOrDefault("text-align", "left");

            float totalTextHeight = element.WrappedTextLines.Count * lineHeight;
            float yOffset = (box.ContentRect.Height - totalTextHeight) / 2f;

            for (int i = 0; i < element.WrappedTextLines.Count; i++)
            {
                var line = element.WrappedTextLines[i];
                float xOffset = 0;
                // *** УЛУЧШЕНИЕ: Добавлена поддержка text-align ***
                if (textAlign is "center" or "right")
                {
                    float textWidth = Raylib.MeasureTextEx(font, line, fontSize, 1).X;
                    if (textAlign == "center") xOffset = (box.ContentRect.Width - textWidth) / 2f;
                    else if (textAlign == "right") xOffset = box.ContentRect.Width - textWidth;
                }

                var position = new Vector2(
                    box.ContentRect.X + xOffset,
                    box.ContentRect.Y + yOffset + (i * lineHeight)
                );
            
                var m = Raylib.MeasureTextEx(font, line, fontSize, 1);
                Raylib.DrawTextEx(font, line, position, fontSize, 1, textColor);
            }
        }
    
        if (hasOverflowHidden) Raylib.EndScissorMode();
    }
    
    private bool IsStackingContext(UIElement element)
    {
        var style = element.ComputedStyle;
        var position = style.GetValueOrDefault(s.position, s.position_static);
        var zIndex = style.GetValueOrDefault(s.z_index, s.auto);
        return (position is s.position_absolute or s.position_relative or s.position_fixed) && zIndex != s.auto;
    }

    #region Вспомогательные методы
    private float ParseFloat(string value, float defaultValue = 0f)
    {
        if (string.IsNullOrWhiteSpace(value)) return defaultValue;
        value = value.Replace("px", "").Trim();
        return float.TryParse(value, out float result) ? result : defaultValue;
    }
    
    private int ParseInt(string value, int defaultValue = 0) => int.TryParse(value, out int result) ? result : defaultValue;

    private Color ParseColor(string value)
    {
        return value?.ToLower().Trim() switch
        {
            s.transparent => Color.Blank,
            "white" => Color.White,
            "black" => Color.Black,
            "red" => Color.Red,
            "blue" => Color.Blue,
            "green" => Color.Green,
            "lightblue" => Color.SkyBlue,
            "gray" => Color.Gray,
            "lightgray" => Color.LightGray,
            "lightyellow" => Color.RayWhite,
            "darkblue" => Color.DarkBlue,
            _ => Color.Blank
        };
    }
    #endregion
}