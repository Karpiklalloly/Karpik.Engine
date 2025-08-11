using System.Numerics;
using Karpik.Engine.Client.UIToolkit;
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
    
    // Виртуальные методы убраны - теперь используется интерфейс ITextProvider
    
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
        var bounds = new Rectangle(Position.X, Position.Y, Size.X, Size.Y);
        
        // Рендерим фон
        if (ResolvedStyle.BackgroundColor?.A > 0)
        {
            if ((ResolvedStyle.BorderRadius ?? 0) > 0)
            {
                // Если BorderRadius больше половины размера - рендерим как круг
                if ((ResolvedStyle.BorderRadius ?? 0) >= Math.Min(Size.X, Size.Y) / 2)
                {
                    var center = new Vector2(Position.X + Size.X / 2, Position.Y + Size.Y / 2);
                    var radius = Math.Min(Size.X, Size.Y) / 2;
                    Raylib.DrawCircleV(center, radius, ResolvedStyle.BackgroundColor.Value);
                }
                else
                {
                    // Рендерим с закругленными углами
                    var roundness = Math.Min(ResolvedStyle.BorderRadius ?? 0, 1f);
                    Raylib.DrawRectangleRounded(bounds, roundness, 8, ResolvedStyle.BackgroundColor.Value);
                }
            }
            else
            {
                // Обычный прямоугольник без закругления
                Raylib.DrawRectangle(
                    (int)Position.X, (int)Position.Y,
                    (int)Size.X, (int)Size.Y,
                    ResolvedStyle.BackgroundColor.Value
                );
            }
        }
        
        // Рендерим рамку
        if ((ResolvedStyle.BorderWidth ?? 0) > 0 && (ResolvedStyle.BorderColor?.A ?? 0) > 0)
        {
            if ((ResolvedStyle.BorderRadius ?? 0) > 0)
            {
                // Если BorderRadius больше половины размера - рендерим как круг
                if ((ResolvedStyle.BorderRadius ?? 0) >= Math.Min(Size.X, Size.Y) / 2)
                {
                    var center = new Vector2(Position.X + Size.X / 2, Position.Y + Size.Y / 2);
                    var radius = Math.Min(Size.X, Size.Y) / 2;
                    Raylib.DrawCircleLinesV(center, radius, ResolvedStyle.BorderColor.Value);
                }
                else
                {
                    // Рамка с закругленными углами - используем встроенную функцию Raylib
                    var roundness = Math.Min(ResolvedStyle.BorderRadius ?? 0, 1);
                    Raylib.DrawRectangleRoundedLinesEx(bounds, roundness, 8, ResolvedStyle.BorderWidth ?? 0, ResolvedStyle.BorderColor.Value);
                }
            }
            else
            {
                // Обычная прямоугольная рамка
                Raylib.DrawRectangleLinesEx(bounds, ResolvedStyle.BorderWidth ?? 0, ResolvedStyle.BorderColor.Value);
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
        
        // Сначала проверяем дочерние элементы (в обратном порядке для правильного z-order)
        for (int i = Children.Count - 1; i >= 0; i--)
        {
            for (int j = 0; j < Children[i]._manipulators.Count; j++)
            {
                var manipulator = Children[i]._manipulators[j];
                // if (manipulator.Handle(inputEvent))
                // {
                //     return true;
                // }
                manipulator.Handle(inputEvent);
            }
            
            if (Children[i].HandleInputEvent(inputEvent))
            {
                return true; // Событие обработано дочерним элементом
            }


        }
        
        // Если событие не обработано дочерними элементами, обрабатываем сами
        bool result = HandleSelfInputEvent(inputEvent);
        return result;
    }
    
    protected virtual bool HandleSelfInputEvent(InputEvent inputEvent)
    {
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

    protected void DrawText(string text)
    {
        if (!string.IsNullOrEmpty(text))
        {
            var fontSize = ResolvedStyle.FontSize ?? 16;
            var textColor = ResolvedStyle.TextColor ?? Color.Black;
            var textSize = Raylib.MeasureText(text, fontSize);
            var textPos = CalculateTextPosition(textSize, ResolvedStyle.TextAlign);
            Raylib.DrawText(text, (int)textPos.X, (int)textPos.Y, fontSize, textColor);
        }
    }

    private Vector2 CalculateTextPosition(int textWidth, AlignText? alignment)
    {
        var x = (alignment ?? AlignText.Left) switch
        {
            AlignText.Left => Position.X + ResolvedStyle.Padding.Left,
            AlignText.Center => Position.X + (Size.X - textWidth) / 2,
            AlignText.Right => Position.X + Size.X - textWidth - ResolvedStyle.Padding.Right,
            _ => Position.X + ResolvedStyle.Padding.Left
        };
        
        var y = Position.Y + (Size.Y - (ResolvedStyle.FontSize ?? 16)) / 2;
        
        return new Vector2(x, y);
    }
}