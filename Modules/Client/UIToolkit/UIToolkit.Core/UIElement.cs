using System.Collections.ObjectModel;

namespace Karpik.Engine.Client.UIToolkit;

[Flags]
public enum DirtyFlag
{
    None = 0,
    Style = 1 << 0,
    Layout = 1 << 1,
}

public class UIElement
{
    public string Id { get; }
    
    public ReadOnlyDictionary<string, bool> ClassesReadOnly => 
        new(new Dictionary<string, bool>(Classes.ToDictionary(c => c, c => true)));
    public HashSet<string> Classes { get; } = new();
    
    public ReadOnlyDictionary<string, string> InlineStylesReadOnly => new(new Dictionary<string, string>(InlineStyles));
    internal Dictionary<string, string> InlineStyles { get; } = new();

    public string Text
    {
        get => field;
        set
        {
            if (Text == value) return;
            field = value ?? string.Empty;
            MarkDirty(DirtyFlag.Layout);
        }
    }
    
    public IReadOnlyList<string> TextLines
    {
        get
        {
            if (WrappedTextLines.Count == 0)
            {
                return new List<string> { Text };
            }
            return WrappedTextLines;
        }
    }
    internal List<string> WrappedTextLines { get; } = [];

    public UIElement Parent { get; private set; }
    public List<UIElement> Children { get; } = [];

    public Dictionary<string, string> ComputedStyle { get; set; } = new();
    public LayoutBox LayoutBox { get; set; } = new();
    
    public bool IsHovered { get; internal set; }
    public bool IsActive { get; internal set; }
    public DirtyFlag Dirty { get; internal set; } = DirtyFlag.Style | DirtyFlag.Layout;
    
    internal IReadOnlyList<IManipulator> Manipulators => _manipulators;
    
    private readonly List<IManipulator> _manipulators = [];

    public UIElement() : this(string.Empty)
    {
    }
    
    public UIElement(string id)
    {
        Id = id;
    }

    public void AddChild(UIElement child)
    {
        Children.Add(child);
        child.Parent = this;
    }
    
    public void RemoveChild(UIElement child)
    {
        if (Children.Remove(child))
        {
            child.Parent = null;
        }
    }
    
    public void AddManipulator(IManipulator manipulator)
    {
        _manipulators.Add(manipulator);
        manipulator.Target = this;
    }
    
    public void RemoveManipulator(IManipulator manipulator)
    {
        if (_manipulators.Remove(manipulator))
        {
            manipulator.Target = null;
        }
    }
    
    public void MarkDirty(DirtyFlag flag)
    {
        if (((int)Dirty & (int)flag) == (int)flag) return;

        Dirty |= flag;

        if (flag.HasFlag(DirtyFlag.Style))
        {
            Dirty |= DirtyFlag.Layout;
        }

        if (flag.HasFlag(DirtyFlag.Layout))
        {
            Parent?.MarkDirty(DirtyFlag.Layout);
        }
        
        if (flag.HasFlag(DirtyFlag.Style))
        {
            foreach (var child in Children)
            {
                child.MarkDirty(DirtyFlag.Style);
            }
        }
    }
    
    public void AddClass(string className)
    {
        if (Classes.Add(className))
        {
            MarkDirty(DirtyFlag.Style);
        }
    }
    
    public void RemoveClass(string className)
    {
        if (Classes.Remove(className))
        {
            MarkDirty(DirtyFlag.Style);
        }
    }

    public void SetInlineStyle(string property, string value)
    {
        if (InlineStyles.TryGetValue(property, out var existingValue) && existingValue == value) return;
        InlineStyles[property] = value;
        MarkDirty(DirtyFlag.Style);
    }
    
    internal void ClearDirtyFlag(DirtyFlag flag)
    {
        Dirty &= ~flag;
    }
}