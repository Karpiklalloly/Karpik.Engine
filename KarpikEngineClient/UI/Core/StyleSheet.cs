using Raylib_cs;

namespace Karpik.Engine.Client.UIToolkit;

public class StyleSheet
{
    private readonly Dictionary<string, Style> _classStyles = new();
    private readonly Dictionary<string, Dictionary<PseudoClass, Style>> _pseudoClassStyles = new();
    
    public void AddClass(string className, Style style)
    {
        _classStyles[className] = style;
    }
    
    public void AddClass(string className, PseudoClass pseudoClass, Style style)
    {
        if (!_pseudoClassStyles.ContainsKey(className))
            _pseudoClassStyles[className] = new Dictionary<PseudoClass, Style>();
            
        _pseudoClassStyles[className][pseudoClass] = style;
    }
    
    public void RemoveClass(string className)
    {
        _classStyles.Remove(className);
        _pseudoClassStyles.Remove(className);
    }
    
    public Style? GetClassStyle(string className)
    {
        return _classStyles.TryGetValue(className, out var style) ? style : null;
    }
    
    public Style? GetPseudoClassStyle(string className, PseudoClass pseudoClass)
    {
        if (_pseudoClassStyles.TryGetValue(className, out var pseudoStyles))
        {
            return pseudoStyles.TryGetValue(pseudoClass, out var style) ? style : null;
        }
        return null;
    }
    
    public Style ComputeStyle(VisualElement element)
    {
        var computedStyle = new Style();
        
        // Применяем базовые стили классов
        foreach (var className in element.Classes)
        {
            var classStyle = GetClassStyle(className);
            if (classStyle != null)
                computedStyle.CopyFrom(classStyle);
        }
        
        // Применяем псевдоклассы в порядке приоритета
        var pseudoClasses = GetActivePseudoClasses(element);
        foreach (var pseudoClass in pseudoClasses)
        {
            foreach (var className in element.Classes)
            {
                var pseudoStyle = GetPseudoClassStyle(className, pseudoClass);
                if (pseudoStyle != null)
                    computedStyle.CopyFrom(pseudoStyle);
            }
        }
        
        return computedStyle;
    }
    
    private static List<PseudoClass> GetActivePseudoClasses(VisualElement element)
    {
        var activeClasses = new List<PseudoClass>();
        
        if (!element.Enabled)
            activeClasses.Add(PseudoClass.Disabled);

        if (element.IsHovered)
        {
            if (element.HasClass("button"))
            {
                
            }
            activeClasses.Add(PseudoClass.Hover);
        }
            
            
        if (element.IsPressed)
            activeClasses.Add(PseudoClass.Active);
            
        if (element.IsFocused)
            activeClasses.Add(PseudoClass.Focus);
            
        // Проверяем позицию в родителе
        if (element.Parent != null)
        {
            var siblings = element.Parent.Children.Where(c => c.Visible).ToList();
            if (siblings.Count > 0)
            {
                if (siblings.First() == element)
                    activeClasses.Add(PseudoClass.FirstChild);
                if (siblings.Last() == element)
                    activeClasses.Add(PseudoClass.LastChild);
            }
        }
        
        return activeClasses;
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
        
        // Псевдоклассы для кнопок
        styleSheet.AddHover("button", new Style 
        { 
            BackgroundColor = new Color(25, 118, 210, 255) // Темнее при наведении
        });
        
        styleSheet.AddActive("button", new Style 
        { 
            BackgroundColor = new Color(13, 71, 161, 255) // Еще темнее при нажатии
        });
        
        styleSheet.AddDisabled("button", new Style 
        { 
            BackgroundColor = new Color(189, 189, 189, 255), // Серый
            TextColor = new Color(158, 158, 158, 255) // Серый текст
        });
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

            foreach (var style in first._pseudoClassStyles)
            {
                foreach (var pseudoStyle in first._pseudoClassStyles[style.Key])
                {
                    s.AddClass(style.Key, pseudoStyle.Key, pseudoStyle.Value);
                }
            }
        }

        if (second != null)
        {
            foreach (var style in second._classStyles)
            {
                s._classStyles[style.Key] = style.Value;
            }
            
            foreach (var style in second._pseudoClassStyles)
            {
                foreach (var pseudoStyle in second._pseudoClassStyles[style.Key])
                {
                    s.AddClass(style.Key, pseudoStyle.Key, pseudoStyle.Value);
                }
            }
        }

        return s;
    }
    
    // Удобные методы для добавления псевдоклассов
    public StyleSheet AddHover(string className, Style hoverStyle)
    {
        AddClass(className, PseudoClass.Hover, hoverStyle);
        return this;
    }
    
    public StyleSheet AddActive(string className, Style activeStyle)
    {
        AddClass(className, PseudoClass.Active, activeStyle);
        return this;
    }
    
    public StyleSheet AddFocus(string className, Style focusStyle)
    {
        AddClass(className, PseudoClass.Focus, focusStyle);
        return this;
    }
    
    public StyleSheet AddDisabled(string className, Style disabledStyle)
    {
        AddClass(className, PseudoClass.Disabled, disabledStyle);
        return this;
    }
}