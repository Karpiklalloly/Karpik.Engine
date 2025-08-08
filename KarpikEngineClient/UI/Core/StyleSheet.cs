using Raylib_cs;

namespace Karpik.Engine.Client.UIToolkit.Core;

public class StyleSheet
{
    private readonly Dictionary<string, Style> _classStyles = new();
    
    public void AddClass(string className, Style style)
    {
        _classStyles[className] = style;
    }
    
    public void RemoveClass(string className)
    {
        _classStyles.Remove(className);
    }
    
    public Style? GetClassStyle(string className)
    {
        return _classStyles.TryGetValue(className, out var style) ? style : null;
    }
    
    public Style ComputeStyle(VisualElement element)
    {
        var computedStyle = new Style();
        
        // Применяем стили классов в порядке добавления
        foreach (var className in element.Classes)
        {
            var classStyle = GetClassStyle(className);
            if (classStyle != null)
                computedStyle.CopyFrom(classStyle);
        }
        
        // Применяем inline стили (они имеют наивысший приоритет)
        computedStyle.CopyFrom(element.Style);
        
        return computedStyle;
    }
    
    // Предустановленные стили
    public static StyleSheet CreateDefault()
    {
        var styleSheet = new StyleSheet();
        
        // Стиль для кнопок
        var buttonStyle = new Style
        {
            Height = 40,
            BackgroundColor = new Color(33, 150, 243, 255),
            TextColor = Color.White,
            BorderRadius = 5,
            Padding = new Padding(15, 10),
            Margin = new Margin(0, 0, 0, 5)
        };
        styleSheet.AddClass("button", buttonStyle);
        
        // Стиль для панелей
        var panelStyle = new Style
        {
            BackgroundColor = new Color(240, 240, 240, 255),
            BorderColor = new Color(200, 200, 200, 255),
            BorderWidth = 1,
            BorderRadius = 8,
            Padding = new Padding(15),
            Margin = new Margin(10)
        };
        styleSheet.AddClass("panel", panelStyle);
        
        // Стиль для заголовков
        var headerStyle = new Style
        {
            Height = 50,
            BackgroundColor = new Color(76, 175, 80, 255),
            TextColor = Color.White,
            FontSize = 18,
            Text = AlignText.Center,
            BorderRadius = 5,
            Margin = new Margin(0, 0, 0, 10)
        };
        styleSheet.AddClass("header", headerStyle);
        
        // Стиль для контента
        var contentStyle = new Style
        {
            BackgroundColor = Color.White,
            BorderColor = new Color(220, 220, 220, 255),
            BorderWidth = 1,
            BorderRadius = 5,
            Padding = new Padding(15),
            FlexGrow = 1
        };
        styleSheet.AddClass("content", contentStyle);
        
        return styleSheet;
    }
}