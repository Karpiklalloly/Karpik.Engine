using Raylib_cs;

namespace Karpik.Engine.Client.UIToolkit;

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
        
        // Применяем только стили классов (inline стили применяются в VisualElement)
        foreach (var className in element.Classes)
        {
            var classStyle = GetClassStyle(className);
            if (classStyle != null)
                computedStyle.CopyFrom(classStyle);
        }
        
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

    public static StyleSheet Combine(StyleSheet first, StyleSheet second)
    {
        StyleSheet s = new();
        if (first != null)
        {
            foreach (var style in first._classStyles)
            {
                s._classStyles[style.Key] = style.Value;
            }
        }

        if (second != null)
        {
            foreach (var style in second._classStyles)
            {
                s._classStyles[style.Key] = style.Value;
            }
        }

        return s;
    }
}