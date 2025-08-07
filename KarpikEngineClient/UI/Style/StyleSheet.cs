namespace Karpik.Engine.Client.UIToolkit;

public class StyleSheet
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
}