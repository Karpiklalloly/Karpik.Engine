namespace Karpik.Engine.Client.UIToolkit;

public class UIElement
{
    public string Id { get; }
    public HashSet<string> Classes { get; } = new();
    public Dictionary<string, string> InlineStyles { get; } = new();
    
    public string Text { get; set; } = "";
    public List<string> WrappedTextLines { get; set; } = new List<string>();

    public UIElement Parent { get; private set; }
    public List<UIElement> Children { get; } = new();

    public Dictionary<string, string> ComputedStyle { get; set; } = new();
    public LayoutBox LayoutBox { get; set; } = new();
    
    public bool IsHovered { get; internal set; }
    public bool IsActive { get; internal set; }
    
    internal IReadOnlyList<IManipulator> Manipulators => _manipulators;
    
    private readonly List<IManipulator> _manipulators = new List<IManipulator>();

    public UIElement(string id = "")
    {
        Id = id;
    }

    public void AddChild(UIElement child)
    {
        Children.Add(child);
        child.Parent = this;
    }
    
    public void AddManipulator(IManipulator manipulator)
    {
        _manipulators.Add(manipulator);
        manipulator.Target = this;
    }
}