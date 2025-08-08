namespace Karpik.Engine.Client.UIToolkit;

public class StyleSheet : ICloneable
{
    public List<StyleRule> Rules { get; } = [];
    
    public void AddRule(StyleRule rule)
    {
        Rules.Add(rule);
    }
    
    public Style GetStylesForElement(VisualElement element)
    {
        var computedStyles = new Style();
        
        // Применяем стили от всех подходящих правил
        foreach (var rule in Rules)
        {
            if (rule.Matches(element))
            {
                rule.ApplyTo(computedStyles);
            }
        }
        
        return computedStyles;
    }

    public object Clone()
    {
        var clone = new StyleSheet();
        foreach (var rule in Rules)
        {
            clone.Rules.Add((StyleRule)rule.Clone());
        }
        return clone;
    }
    
    public void CopyFrom(StyleSheet other)
    {
        Rules.Clear();
        foreach (var rule in other.Rules)
        {
            Rules.Add((StyleRule)rule.Clone());
        }
    }
}