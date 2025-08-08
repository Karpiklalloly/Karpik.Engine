using System.Numerics;
using Karpik.Engine.Client.UIToolkit;
using Raylib_cs;

namespace Karpik.Engine.Client.UIToolkit;

public class VisualElement
{
    public string Name { get; set; }
    public string? ClassList { get; set; }
    public Vector2 Position { get; set; }
    public Vector2 Size { get; set; }
    public bool Visible { get; set; } = true;
    public bool Enabled { get; set; } = true;
    
    public VisualElement? Parent { get; set; }
    public List<VisualElement> Children { get; } = [];
    
    // Стили
    public Style Style { get; private set; } = new();
    public Style ResolvedStyle => GetComputedStyle();
    public StyleSheet StyleSheet { get; private set; } = new();
    
    // Состояния для псевдоклассов
    public bool IsHovered { get; private set; } = false;
    public bool IsActive { get; private set; } = false;
    public bool IsFocused { get; private set; } = false;
    
    // Манипуляторы
    private List<IManipulator> _manipulators = new List<IManipulator>();
    
    // События
    public event Action<MouseEvent>? OnMouseDown;
    public event Action<MouseEvent>? OnMouseUp;
    public event Action<MouseEvent>? OnClick;
    public event Action? OnFocus;
    public event Action? OnBlur;
    
    public VisualElement(string name = "VisualElement")
    {
        Name = name;
    }
    
    public void AddChild(VisualElement child)
    {
        if (child == null) return;
        
        Children.Add(child);
        child.Parent = this;
    }
    
    public void RemoveChild(VisualElement child)
    {
        if (child == null) return;
        
        Children.Remove(child);
        child.Parent = null;
    }
    
    public virtual void ApplyStyles()
    {
        var computedStyle = GetComputedStyle();
        
        // Применяем размеры из стилей
        ApplySizeStyles(computedStyle);
    }
    
    private void ApplySizeStyles(Style computedStyle)
    {
        Vector2 newSize = Size;
        
        // Применяем ширину
        if (computedStyle.Width.IsSet)
        {
            newSize.X = computedStyle.Width.Value;
        }
        
        // Применяем высоту
        if (computedStyle.Height.IsSet)
        {
            newSize.Y = computedStyle.Height.Value;
        }
        
        // Применяем ограничения (всегда, независимо от основных размеров)
        if (computedStyle.MinWidth.IsSet)
        {
            newSize.X = Math.Max(newSize.X, computedStyle.MinWidth.Value);
        }
        if (computedStyle.MaxWidth.IsSet)
        {
            newSize.X = Math.Min(newSize.X, computedStyle.MaxWidth.Value);
        }
        if (computedStyle.MinHeight.IsSet)
        {
            newSize.Y = Math.Max(newSize.Y, computedStyle.MinHeight.Value);
        }
        if (computedStyle.MaxHeight.IsSet)
        {
            newSize.Y = Math.Min(newSize.Y, computedStyle.MaxHeight.Value);
        }
        
        Size = newSize;
    }
    
    public Style GetComputedStyle()
    {
        // Базовые стили элемента
        var computed = new Style();
    
        // Получаем StyleSheet от корня через родителей
        var styleSheet = GetRootStyleSheet();
    
        // Если есть StyleSheet, применяем его правила
        if (styleSheet != null)
        {
            // Применяем базовые стили
            ApplyStyleSheetRules(styleSheet, computed, "");
        
            // Применяем псевдоклассы
            if (IsHovered)
                ApplyStyleSheetRules(styleSheet, computed, ":hover");
            if (IsActive)
                ApplyStyleSheetRules(styleSheet, computed, ":active");
            if (!Enabled)
                ApplyStyleSheetRules(styleSheet, computed, ":disabled");
            if (IsFocused)
                ApplyStyleSheetRules(styleSheet, computed, ":focus");
        }
    
        // Применяем inline-стили (они имеют больший приоритет)
        ApplyInlineStyles(computed);
    
        return computed;
    }
    
    private StyleSheet? GetRootStyleSheet()
    {
        // Ищем StyleSheet у себя или у родителей
        var current = this;
        while (current != null)
        {
            if (current.StyleSheet != null)
                return current.StyleSheet;
            current = current.Parent;
        }
        return null;
    }
    
    // 📋 Применение стилей из StyleSheet
    private void ApplyStyleSheetRules(StyleSheet styleSheet, Style target, string pseudoClass)
    {
        foreach (var rule in styleSheet.Rules)
        {
            // Проверяем, подходит ли правило для этого элемента
            if (rule.Matches(this))
            {
                // Если это псевдокласс, применяем соответствующие стили
                if (!string.IsNullOrEmpty(pseudoClass) && rule.PseudoClasses.ContainsKey(pseudoClass))
                {
                    rule.PseudoClasses[pseudoClass].ApplyTo(target);
                }
                else if (string.IsNullOrEmpty(pseudoClass))
                {
                    // Применяем основные стили
                    rule.ApplyTo(target);
                }
            }
        }
    }

    private void ApplyInlineStyles(Style target)
    {
        // Размеры и ограничения
        if (Style.Width.IsSet) target.Width = Style.Width;
        if (Style.Height.IsSet) target.Height = Style.Height;
        if (Style.MinWidth.IsSet) target.MinWidth = Style.MinWidth;
        if (Style.MaxWidth.IsSet) target.MaxWidth = Style.MaxWidth;
        if (Style.MinHeight.IsSet) target.MinHeight = Style.MinHeight;
        if (Style.MaxHeight.IsSet) target.MaxHeight = Style.MaxHeight;

        // Flexbox свойства
        if (Style.FlexGrow.IsSet) target.FlexGrow = Style.FlexGrow;
        if (Style.FlexShrink.IsSet) target.FlexShrink = Style.FlexShrink;
        if (Style.FlexBasis.IsSet) target.FlexBasis = Style.FlexBasis;

        // Layout свойства
        if (Style.FlexDirection.IsSet) target.FlexDirection = Style.FlexDirection;
        if (Style.JustifyContent.IsSet) target.JustifyContent = Style.JustifyContent;
        if (Style.AlignItems.IsSet) target.AlignItems = Style.AlignItems;
        if (Style.AlignSelf.IsSet) target.AlignSelf = Style.AlignSelf;

        // Цвета и оформление
        if (Style.BackgroundColor.IsSet) target.BackgroundColor = Style.BackgroundColor;
        if (Style.BorderColor.IsSet) target.BorderColor = Style.BorderColor;
        if (Style.Color.IsSet) target.Color = Style.Color;

        // Размеры и отступы
        if (Style.BorderWidth.IsSet) target.BorderWidth = Style.BorderWidth;
        if (Style.BorderRadius.IsSet) target.BorderRadius = Style.BorderRadius;

        // Padding
        if (Style.PaddingTop.IsSet) target.PaddingTop = Style.PaddingTop;
        if (Style.PaddingRight.IsSet) target.PaddingRight = Style.PaddingRight;
        if (Style.PaddingBottom.IsSet) target.PaddingBottom = Style.PaddingBottom;
        if (Style.PaddingLeft.IsSet) target.PaddingLeft = Style.PaddingLeft;

        // Margin
        if (Style.MarginTop.IsSet) target.MarginTop = Style.MarginTop;
        if (Style.MarginRight.IsSet) target.MarginRight = Style.MarginRight;
        if (Style.MarginBottom.IsSet) target.MarginBottom = Style.MarginBottom;
        if (Style.MarginLeft.IsSet) target.MarginLeft = Style.MarginLeft;

        // Текст
        if (Style.FontSize.IsSet) target.FontSize = Style.FontSize;
        if (Style.TextAlign.IsSet) target.TextAlign = Style.TextAlign;
        
        if (Style.Position.IsSet) target.Position = Style.Position;
        if (Style.Top.IsSet) target.Top = Style.Top;
        if (Style.Right.IsSet) target.Right = Style.Right;
        if (Style.Bottom.IsSet) target.Bottom = Style.Bottom;
        if (Style.Left.IsSet) target.Left = Style.Left;
    
        if (Style.Padding.IsSet) target.Padding = Style.Padding;
        if (Style.Margin.IsSet) target.Margin = Style.Margin;
    
        if (Style.BoxSizing.IsSet) target.BoxSizing = Style.BoxSizing;
    }

    public virtual void Update(double deltaTime)
    {
        // Рассчитываем layout только для корневого элемента
        if (Parent == null)
        {
            var availableSpace = new Rectangle(0, 0, Raylib.GetRenderWidth(), Raylib.GetRenderHeight());
            LayoutEngine.CalculateLayout(this, availableSpace);
        }
        
        // Обновляем всех детей
        foreach (var child in Children)
        {
            child.Update(deltaTime);
        }
    }
    
    public virtual void Render()
    {
        if (!Visible) return;
    
        var computedStyle = GetComputedStyle();
    
        // Применяем padding для расчета внутренней области
        Rectangle contentRect = CalculateContentRect(computedStyle);
    
        // Рендерим фон с учетом padding
        if (computedStyle.BackgroundColor.IsSet && computedStyle.BackgroundColor.Value.A > 0)
        {
            var bgColor = computedStyle.BackgroundColor.Value;
            Raylib.DrawRectangle(
                (int)contentRect.X, (int)contentRect.Y,
                (int)contentRect.Width, (int)contentRect.Height, 
                bgColor);
        }
    
        // Рендерим рамку (внешнюю, вокруг padding)
        if (computedStyle.BorderWidth.IsSet && computedStyle.BorderWidth.Value > 0 && 
            computedStyle.BorderColor.IsSet && computedStyle.BorderColor.Value.A > 0)
        {
            Rectangle borderRect = new Rectangle(
                Position.X, Position.Y, 
                Size.X, Size.Y);
            
            Raylib.DrawRectangleLinesEx(
                borderRect,
                computedStyle.BorderWidth.Value, 
                computedStyle.BorderColor.Value);
        }
    
        // Рендерим детей
        foreach (var child in Children)
        {
            child.Render();
        }
    }
    
    public void CalculateLayout()
    {
        var availableSpace = new Rectangle(
            Position.X, Position.Y, 
            Size.X, Size.Y
        );
        
        LayoutEngine.CalculateLayout(this, availableSpace);
    }
    
    // Работа с манипуляторами
    public void AddManipulator(IManipulator manipulator)
    {
        if (manipulator == null || _manipulators.Contains(manipulator)) return;
        
        _manipulators.Add(manipulator);
        manipulator.Attach(this);
    }
    
    public void RemoveManipulator(IManipulator manipulator)
    {
        if (manipulator == null || !_manipulators.Contains(manipulator)) return;
        
        _manipulators.Remove(manipulator);
        manipulator.Detach(this);
    }
    
    // Методы для вызова событий
    public virtual void HandleMouseDown(MouseEvent mouseEvent)
    {
        IsActive = true;
        OnMouseDown?.Invoke(mouseEvent);
    }
    
    public virtual void HandleMouseUp(MouseEvent mouseEvent)
    {
        IsActive = false;
        OnMouseUp?.Invoke(mouseEvent);
    }
    
    public virtual void HandleClick(MouseEvent mouseEvent)
    {
        OnClick?.Invoke(mouseEvent);
    }
    
    public virtual void HandleFocus()
    {
        IsFocused = true;
        OnFocus?.Invoke();
    }
    
    public virtual void HandleBlur()
    {
        IsFocused = false;
        OnBlur?.Invoke();
    }
    
    public virtual void HandleMouseEnter()
    {
        IsHovered = true;
    }
    
    public virtual void HandleMouseLeave()
    {
        IsHovered = false;
        IsActive = false;
    }
    
    // Вспомогательные методы для работы со стилями
    public bool HasClass(string className)
    {
        if (string.IsNullOrEmpty(ClassList)) return false;
        var classes = ClassList.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return Array.IndexOf(classes, className) >= 0;
    }
    
    public void AddClass(string className)
    {
        if (string.IsNullOrEmpty(ClassList))
        {
            ClassList = className;
        }
        else if (!HasClass(className))
        {
            ClassList += " " + className;
        }
    }
    
    public void RemoveClass(string className)
    {
        if (string.IsNullOrEmpty(ClassList)) return;
        
        var classes = ClassList.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var newClasses = new List<string>();
        
        foreach (var cls in classes)
        {
            if (cls != className)
                newClasses.Add(cls);
        }
        
        ClassList = string.Join(" ", newClasses);
    }
    
    private Rectangle CalculateContentRect(Style style)
    {
        float paddingLeft = style.PaddingLeft.IsSet ? style.PaddingLeft.Value : 0;
        float paddingRight = style.PaddingRight.IsSet ? style.PaddingRight.Value : 0;
        float paddingTop = style.PaddingTop.IsSet ? style.PaddingTop.Value : 0;
        float paddingBottom = style.PaddingBottom.IsSet ? style.PaddingBottom.Value : 0;
    
        return new Rectangle(
            Position.X + paddingLeft,
            Position.Y + paddingTop,
            Size.X - paddingLeft - paddingRight,
            Size.Y - paddingTop - paddingBottom
        );
    }
}