using System.Numerics;
using Raylib_cs;

namespace Karpik.Engine.Client.UIToolkit;

public class VisualElement
{
    public string Name { get; set; }
    public Vector2 Position { get; set; }
    public Vector2 Size { get; set; }
    public bool Visible { get; set; } = true;
    public bool Enabled { get; set; } = true;
    
    public VisualElement? Parent { get; private set; }
    public List<VisualElement> Children { get; } = new();
    
    public Style Style { get; } = new();
    public List<string> Classes { get; } = new();
    public StyleSheet StyleSheet { get; set; } = new();
    
    private readonly List<IManipulator> _manipulators = new();
    
    public bool IsHovered { get; private set; }
    public bool IsFocused { get; private set; }
    public bool IsPressed { get; private set; }
    
    public VisualElement(string name = "UIElement")
    {
        Name = name;
    }
    
    public void AddChild(VisualElement child)
    {
        if (child.Parent != null)
            child.Parent.RemoveChild(child);
            
        Children.Add(child);
        child.Parent = this;
    }
    
    public void RemoveChild(VisualElement child)
    {
        if (Children.Remove(child))
            child.Parent = null;
    }
    
    public void AddClass(string className)
    {
        if (!Classes.Contains(className))
            Classes.Add(className);
    }
    
    public void RemoveClass(string className)
    {
        Classes.Remove(className);
    }
    
    public bool HasClass(string className)
    {
        return Classes.Contains(className);
    }
    
    // Управление манипуляторами
    public void AddManipulator(IManipulator manipulator)
    {
        if (!_manipulators.Contains(manipulator))
        {
            _manipulators.Add(manipulator);
            manipulator.Attach(this);
        }
    }
    
    public T? GetManipulator<T>() where T : IManipulator
    {
        return (T?)_manipulators.FirstOrDefault(x => x.GetType() == typeof(T));
    }
    
    public void RemoveManipulator(IManipulator manipulator)
    {
        if (_manipulators.Remove(manipulator))
            manipulator.Detach(this);
    }
    
    public virtual void Update(float deltaTime)
    {
        foreach (var manipulator in _manipulators)
            manipulator.Update(deltaTime);
            
        foreach (var child in Children)
            child.Update(deltaTime);
    }
    
    public virtual void Render()
    {
        if (!Visible) return;
        
        RenderSelf();
        
        foreach (var child in Children)
            child.Render();
    }
    
    protected virtual void RenderSelf()
    {
        // Рендерим фон
        if (Style.BackgroundColor.A > 0)
        {
            // Если BorderRadius больше половины размера - рендерим как круг
            if (Style.BorderRadius >= Math.Min(Size.X, Size.Y) / 2)
            {
                var center = new Vector2(Position.X + Size.X / 2, Position.Y + Size.Y / 2);
                var radius = Math.Min(Size.X, Size.Y) / 2;
                Raylib.DrawCircleV(center, radius, Style.BackgroundColor);
            }
            else
            {
                Raylib.DrawRectangle(
                    (int)Position.X, (int)Position.Y,
                    (int)Size.X, (int)Size.Y,
                    Style.BackgroundColor
                );
            }
        }
        
        // Рендерим рамку
        if (Style.BorderWidth > 0 && Style.BorderColor.A > 0)
        {
            // Если BorderRadius больше половины размера - рендерим как круг
            if (Style.BorderRadius >= Math.Min(Size.X, Size.Y) / 2)
            {
                var center = new Vector2(Position.X + Size.X / 2, Position.Y + Size.Y / 2);
                var radius = Math.Min(Size.X, Size.Y) / 2;
                Raylib.DrawCircleLinesV(center, radius, Style.BorderColor);
            }
            else
            {
                Raylib.DrawRectangleLinesEx(
                    new Rectangle(Position.X, Position.Y, Size.X, Size.Y),
                    Style.BorderWidth,
                    Style.BorderColor
                );
            }
        }
    }
    
    public virtual void HandleClick()
    {
        
    }
    
    public virtual void HandleHover(bool isHovered)
    {
        if (IsHovered != isHovered)
        {
            IsHovered = isHovered;
        }
    }
    
    public virtual void HandleFocus(bool isFocused)
    {
        if (IsFocused != isFocused)
        {
            IsFocused = isFocused;
        }
    }
    
    public virtual void HandlePress(bool isPressed)
    {
        IsPressed = isPressed;
    }
    
    public bool ContainsPoint(Vector2 point)
    {
        return point.X >= Position.X && point.X <= Position.X + Size.X &&
               point.Y >= Position.Y && point.Y <= Position.Y + Size.Y;
    }
    
    public Rectangle GetBounds()
    {
        return new Rectangle(Position.X, Position.Y, Size.X, Size.Y);
    }
    
    // Вычисление стилей с учетом иерархии StyleSheet
    public Style ComputeStyle()
    {
        var computedStyle = new Style();
        
        var styleSheets = GetAllStyleSheetsInHierarchy();
        
        foreach (var styleSheet in styleSheets)
        {
            var tempStyle = styleSheet.ComputeStyle(this);
            computedStyle.CopyFrom(tempStyle);
        }
        
        computedStyle.CopyFrom(Style);
        
        return computedStyle;
    }
    
    private List<StyleSheet> GetAllStyleSheetsInHierarchy()
    {
        var hierarchy = new List<VisualElement>();
        
        var current = this;
        while (current != null)
        {
            hierarchy.Add(current);
            current = current.Parent;
        }
        
        hierarchy.Reverse();

        return hierarchy.Select(element => element.StyleSheet).ToList();
    }
}