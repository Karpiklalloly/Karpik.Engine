namespace Karpik.Engine.Client.UIToolkit;

public class StyleComputer
{
    private static readonly HashSet<string> InheritableProperties = new()
    {
        "color", "font-family", "font-size", "font-weight", "line-height", "text-align"
    };
    
    public void ComputeStyles(UIElement root, StyleSheet styleSheet)
    {
        ComputeStylesForNode(root, styleSheet);
    }

    private void ComputeStylesForNode(UIElement element, StyleSheet styleSheet)
    {
        var computed = new Dictionary<string, string>();
        if (element.Parent != null)
        {
            foreach (var prop in InheritableProperties)
            {
                if (element.Parent.ComputedStyle.TryGetValue(prop, out var value))
                {
                    computed[prop] = value;
                }
            }
        }

        var applicableRules = new List<StyleRule>();
        foreach (var rule in styleSheet.Rules)
        {
            if (DoesSelectorMatch(rule.Selector, element))
            {
                applicableRules.Add(rule);
            }
        }
        applicableRules.Sort((a, b) => a.Selector.CompareTo(b.Selector));

        foreach (var rule in applicableRules)
        {
            foreach (var prop in rule.Properties)
            {
                computed[prop.Key] = prop.Value;
            }
        }
        
        foreach (var prop in element.InlineStyles)
        {
            computed[prop.Key] = prop.Value;
        }

        element.ComputedStyle = computed;

        foreach (var child in element.Children)
        {
            ComputeStylesForNode(child, styleSheet);
        }
    }

    // *** КЛЮЧЕВОЕ ИЗМЕНЕНИЕ ***
    // Проверка на тег удалена.
    private bool DoesSelectorMatch(Selector selector, UIElement element)
    {
        var s = selector.Raw;
        if (s.StartsWith("#"))
        {
            return element.Id == s.Substring(1);
        }
        if (s.StartsWith("."))
        {
            return element.Classes.Contains(s.Substring(1));
        }
        return false;
    }
}