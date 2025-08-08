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
    
    // Стили
    public Style Style { get; } = new();
    public List<string> Classes { get; } = new();
    public StyleSheet? StyleSheet { get; set; }
    
    // Манипуляторы
    private readonly List<IManipulator> _manipulators = new();
    
    // Состояние
    public bool IsHovered { get; private set; }
    public bool IsFocused { get; private set; }
    public bool IsPressed { get; private set; }
    
    public VisualElement(string name = "UIElement")
    {
        Name = name;
    }
    
    // Управление иерархией
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
    
    // Управление классами
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
    
    // Обновление и рендеринг
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
    
    // Обработка событий
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
    
    // Утилиты
    public bool ContainsPoint(Vector2 point)
    {
        return point.X >= Position.X && point.X <= Position.X + Size.X &&
               point.Y >= Position.Y && point.Y <= Position.Y + Size.Y;
    }
    
    public Rectangle GetBounds()
    {
        return new Rectangle(Position.X, Position.Y, Size.X, Size.Y);
    }
    
    // Получение эффективного StyleSheet (свой или родительский)
    public StyleSheet? GetEffectiveStyleSheet()
    {
        // Сначала проверяем свой StyleSheet
        if (StyleSheet != null)
            return StyleSheet;
            
        // Если нет своего, ищем у родителей
        var current = Parent;
        while (current != null)
        {
            if (current.StyleSheet != null)
                return current.StyleSheet;
            current = current.Parent;
        }
        
        return null;
    }
    
    // Вычисление стилей с учетом иерархии StyleSheet
    public Style ComputeStyle()
    {
        var computedStyle = new Style();
        
        // Собираем все StyleSheet от корня к текущему элементу
        var styleSheets = GetAllStyleSheetsInHierarchy();
        
        // Применяем стили в порядке от корня к листу (каскадно)
        foreach (var styleSheet in styleSheets)
        {
            var tempStyle = styleSheet.ComputeStyle(this);
            computedStyle.CopyFrom(tempStyle);
        }
        
        // Применяем inline стили (наивысший приоритет)
        computedStyle.CopyFrom(Style);
        
        return computedStyle;
    }
    
    // Получение всех StyleSheet в иерархии от корня к текущему элементу
    private List<StyleSheet> GetAllStyleSheetsInHierarchy()
    {
        var styleSheets = new List<StyleSheet>();
        var hierarchy = new List<VisualElement>();
        
        // Собираем путь от корня к текущему элементу
        var current = this;
        while (current != null)
        {
            hierarchy.Add(current);
            current = current.Parent;
        }
        
        // Переворачиваем, чтобы идти от корня к листу
        hierarchy.Reverse();
        
        // Собираем StyleSheet в правильном порядке
        foreach (var element in hierarchy)
        {
            if (element.StyleSheet != null)
                styleSheets.Add(element.StyleSheet);
        }
        
        return styleSheets;
    }
    
    // Удобные методы для работы со StyleSheet
    public void SetStyleSheet(StyleSheet styleSheet)
    {
        StyleSheet = styleSheet;
    }
    
    public void ApplyStyleSheet(StyleSheet styleSheet, bool recursive = false)
    {
        StyleSheet = styleSheet;
        
        if (recursive)
        {
            foreach (var child in Children)
                child.ApplyStyleSheet(styleSheet, true);
        }
    }
    
    public void ClearStyleSheet(bool recursive = false)
    {
        StyleSheet = null;
        
        if (recursive)
        {
            foreach (var child in Children)
                child.ClearStyleSheet(true);
        }
    }
    
    // Отладочный метод - получить информацию о применяемых стилях
    public string GetStyleDebugInfo()
    {
        var info = new List<string>();
        var styleSheets = GetAllStyleSheetsInHierarchy();
        
        info.Add($"Element: {Name}");
        info.Add($"Classes: [{string.Join(", ", Classes)}]");
        info.Add($"StyleSheets in hierarchy: {styleSheets.Count}");
        
        for (int i = 0; i < styleSheets.Count; i++)
        {
            var owner = GetStyleSheetOwner(styleSheets[i]);
            info.Add($"  {i + 1}. StyleSheet from: {owner?.Name ?? "Unknown"}");
        }
        
        return string.Join("\n", info);
    }
    
    private VisualElement? GetStyleSheetOwner(StyleSheet targetStyleSheet)
    {
        var current = this;
        while (current != null)
        {
            if (current.StyleSheet == targetStyleSheet)
                return current;
            current = current.Parent;
        }
        return null;
    }
}