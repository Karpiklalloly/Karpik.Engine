namespace Karpik.Engine.Client.UIToolkit;

public class StyleRule
{
    public Selector Selector { get; }
    public Dictionary<string, string> Properties { get; } = new();

    public StyleRule(Selector selector)
    {
        Selector = selector;
    }
}