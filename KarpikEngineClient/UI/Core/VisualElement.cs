using System.Numerics;
using Karpik.Engine.Client.UIToolkit.Manipulators;
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
    
    // Стили
    public Style Style { get; } = new();
    public List<string> Classes { get; } = new();
    public StyleSheet StyleSheet { get; set; } = new();
    
    // Финальные вычисленные стили (обновляются в LayoutEngine)
    public Style ResolvedStyle { get; private set; } = new();
    
    private readonly List<IManipulator> _manipulators = new();
    private readonly AnimationManager _animationManager = new();
    
    public bool IsHovered { get; private set; }
    public bool IsFocused { get; private set; }
    public bool IsPressed { get; private set; }
    
    public VisualElement(string name = "UIElement")
    {
        Name = name;
        AddManipulator(new HoverEffectManipulator());
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
        _animationManager.Update(deltaTime);
        
        foreach (var manipulator in _manipulators)
            manipulator.Update(deltaTime);
            
        foreach (var child in Children.ToArray())
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
        if (ResolvedStyle.BackgroundColor.A > 0)
        {
            // Если BorderRadius больше половины размера - рендерим как круг
            if (ResolvedStyle.BorderRadius >= Math.Min(Size.X, Size.Y) / 2)
            {
                var center = new Vector2(Position.X + Size.X / 2, Position.Y + Size.Y / 2);
                var radius = Math.Min(Size.X, Size.Y) / 2;
                Raylib.DrawCircleV(center, radius, ResolvedStyle.BackgroundColor);
            }
            else
            {
                Raylib.DrawRectangle(
                    (int)Position.X, (int)Position.Y,
                    (int)Size.X, (int)Size.Y,
                    ResolvedStyle.BackgroundColor
                );
            }
        }
        
        // Рендерим рамку
        if (ResolvedStyle.BorderWidth > 0 && ResolvedStyle.BorderColor.A > 0)
        {
            // Если BorderRadius больше половины размера - рендерим как круг
            if (ResolvedStyle.BorderRadius >= Math.Min(Size.X, Size.Y) / 2)
            {
                var center = new Vector2(Position.X + Size.X / 2, Position.Y + Size.Y / 2);
                var radius = Math.Min(Size.X, Size.Y) / 2;
                Raylib.DrawCircleLinesV(center, radius, ResolvedStyle.BorderColor);
            }
            else
            {
                Raylib.DrawRectangleLinesEx(
                    new Rectangle(Position.X, Position.Y, Size.X, Size.Y),
                    ResolvedStyle.BorderWidth,
                    ResolvedStyle.BorderColor
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
    
    public virtual bool HandleInputEvent(InputEvent inputEvent)
    {
        if (!Visible || !Enabled) return false;
        
        // Отладочная информация для кликов
        if (inputEvent.Type == InputEventType.MouseClick)
        {
            Console.WriteLine($"VisualElement {Name}: Handling MouseClick event at {inputEvent.MousePosition}");
        }
        
        // Сначала проверяем дочерние элементы (в обратном порядке для правильного z-order)
        for (int i = Children.Count - 1; i >= 0; i--)
        {
            if (Children[i].HandleInputEvent(inputEvent))
            {
                if (inputEvent.Type == InputEventType.MouseClick)
                {
                    Console.WriteLine($"VisualElement {Name}: Event handled by child {Children[i].Name}");
                }
                return true; // Событие обработано дочерним элементом
            }
        }
        
        // Если событие не обработано дочерними элементами, обрабатываем сами
        bool result = HandleSelfInputEvent(inputEvent);
        if (inputEvent.Type == InputEventType.MouseClick)
        {
            Console.WriteLine($"VisualElement {Name}: HandleSelfInputEvent returned {result}");
        }
        return result;
    }
    
    protected virtual bool HandleSelfInputEvent(InputEvent inputEvent)
    {
        // Проверяем, попадает ли событие мыши в этот элемент
        if (inputEvent.Type == InputEventType.MouseMove ||
            inputEvent.Type == InputEventType.MouseDown ||
            inputEvent.Type == InputEventType.MouseUp ||
            inputEvent.Type == InputEventType.MouseClick)
        {
            bool isInBounds = ContainsPoint(inputEvent.MousePosition);
            
            if (!isInBounds) return false;
            
            // Обрабатываем события мыши
            switch (inputEvent.Type)
            {
                case InputEventType.MouseMove:
                    HandleHover(true);
                    break;
                    
                case InputEventType.MouseDown:
                    if (inputEvent.MouseButton == MouseButton.Left)
                    {
                        HandlePress(true);
                    }
                    break;
                    
                case InputEventType.MouseUp:
                    if (inputEvent.MouseButton == MouseButton.Left)
                    {
                        HandlePress(false);
                    }
                    break;
                    
                case InputEventType.MouseClick:
                    if (inputEvent.MouseButton == MouseButton.Left)
                    {
                        HandleClick();
                        return true; // Клик обработан
                    }
                    break;
            }
        }
        
        return false;
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

        if (HasClass("button") && !IsHovered)
        {
            
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
    
    // Методы для работы с анимациями
    public void AddAnimation(Animation animation)
    {
        _animationManager.AddAnimation(animation);
    }
    
    public void FadeIn(float duration = 0.3f, Action? onComplete = null)
    {
        var animation = Animation.FadeIn(this, duration, onComplete);
        AddAnimation(animation);
    }
    
    public void FadeOut(float duration = 0.3f, Action? onComplete = null)
    {
        var animation = Animation.FadeOut(this, duration, onComplete);
        AddAnimation(animation);
    }
    
    public void SlideIn(Vector2 fromOffset, float duration = 0.3f, Action? onComplete = null)
    {
        var animation = Animation.SlideIn(this, fromOffset, duration, onComplete);
        AddAnimation(animation);
    }
    
    public void ScaleIn(float duration = 0.3f, Action? onComplete = null)
    {
        var animation = Animation.Scale(this, Vector2.Zero, Vector2.One, duration, onComplete);
        AddAnimation(animation);
    }
}