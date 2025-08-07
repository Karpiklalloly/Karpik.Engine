using System.Numerics;
using Karpik.Engine.Client.UIToolkit;
using Raylib_cs;

public class VisualElement
{
    public string Name { get; set; }
    public string ClassList { get; set; }
    public Vector2 Position { get; set; }
    public Vector2 Size { get; set; }
    public bool Visible { get; set; } = true;
    public bool Enabled { get; set; } = true;
    
    public VisualElement Parent { get; set; }
    public List<VisualElement> Children { get; } = [];
    
    // Стили
    public Style Style { get; private set; } = new();
    public StyleSheet StyleSheet { get; private set; } = new();
    
    // Состояния для псевдоклассов
    public bool IsHovered { get; private set; } = false;
    public bool IsActive { get; private set; } = false;
    public bool IsFocused { get; private set; } = false;
    
    // Манипуляторы
    private List<IManipulator> _manipulators = new List<IManipulator>();
    
    // События
    public event Action<MouseEvent> OnMouseDown;
    public event Action<MouseEvent> OnMouseUp;
    public event Action<MouseEvent> OnClick;
    public event Action OnFocus;
    public event Action OnBlur;
    
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
        else if (computedStyle.MinWidth.IsSet)
        {
            newSize.X = Math.Max(newSize.X, computedStyle.MinWidth.Value);
        }
        else if (computedStyle.MaxWidth.IsSet)
        {
            newSize.X = Math.Min(newSize.X, computedStyle.MaxWidth.Value);
        }
        
        // Применяем высоту
        if (computedStyle.Height.IsSet)
        {
            newSize.Y = computedStyle.Height.Value;
        }
        else if (computedStyle.MinHeight.IsSet)
        {
            newSize.Y = Math.Max(newSize.Y, computedStyle.MinHeight.Value);
        }
        else if (computedStyle.MaxHeight.IsSet)
        {
            newSize.Y = Math.Min(newSize.Y, computedStyle.MaxHeight.Value);
        }
        
        Size = newSize;
    }
    
    public Style GetComputedStyle()
    {
        var computed = new Style();
        
        if (StyleSheet != null)
        {
            // Применяем базовые стили
            ApplyStyleSheetRules(StyleSheet, computed, "");
            
            // Применяем псевдоклассы
            if (IsHovered)
                ApplyStyleSheetRules(StyleSheet, computed, ":hover");
            if (IsActive)
                ApplyStyleSheetRules(StyleSheet, computed, ":active");
            if (!Enabled)
                ApplyStyleSheetRules(StyleSheet, computed, ":disabled");
            if (IsFocused)
                ApplyStyleSheetRules(StyleSheet, computed, ":focus");
        }
        
        // Применяем inline-стили (они имеют больший приоритет)
        ApplyInlineStyles(computed);
        
        return computed;
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
    }

    public virtual void Update(double deltaTime)
    {
        // Применяем стили каждый кадр (или можно оптимизировать)
        ApplyStyles();
        
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
        
        // Рендерим фон
        if (computedStyle.BackgroundColor.IsSet && computedStyle.BackgroundColor.Value.A > 0)
        {
            var bgColor = computedStyle.BackgroundColor.Value;
            Raylib.DrawRectangle((int)Position.X, (int)Position.Y, 
                               (int)Size.X, (int)Size.Y, bgColor);
        }
        
        // Рендерим рамку
        if (computedStyle.BorderWidth.IsSet && computedStyle.BorderWidth.Value > 0 && 
            computedStyle.BorderColor.IsSet && computedStyle.BorderColor.Value.A > 0)
        {
            Raylib.DrawRectangleLinesEx(
                new Rectangle(Position.X, Position.Y, Size.X, Size.Y),
                computedStyle.BorderWidth.Value, computedStyle.BorderColor.Value);
        }
        
        // Рендерим детей
        foreach (var child in Children)
        {
            child.Render();
        }
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
}