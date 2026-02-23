namespace Karpik.Engine.Client.UIToolkit;

using s = StyleSheet;

public static class StyleExtensions
{
    public static string GetPosition(this UIElement element)
    {
        return element.ComputedStyle.GetValueOrDefault(s.position, s.position_static);
    }
    
    public static string GetDisplay(this UIElement element)
    {
        return element.ComputedStyle.GetValueOrDefault(s.display, s.display_block);
    }
    
    public static bool IsRow(this UIElement element)
    {
        return element.ComputedStyle.GetValueOrDefault(s.flex_direction, s.flex_direction_row).StartsWith(s.flex_direction_row);
    }
    
    public static string GetJustifyContent(this UIElement element)
    {
        return element.ComputedStyle.GetValueOrDefault(s.justify_content, s.justify_content_flex_start);
    }
}