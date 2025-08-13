using System.Numerics;
using Raylib_cs;

namespace Karpik.Engine.Client.UIToolkit;

/// <summary>
/// Складывающийся контейнер с заголовком и содержимым
/// </summary>
public class Foldout : VisualElement
{
    public string Title { get; set; }
    public bool IsExpanded { get; set; } = false;
    public bool AnimateToggle { get; set; } = true;
    
    private Button? _toggleButton;
    private VisualElement? _contentContainer;
    private float _targetHeight = 0f;
    private float _currentHeight = 0f;
    private readonly float _animationSpeed = 8f;
    
    public event Action<bool>? OnToggle;
    
    public Foldout(string title, bool expanded = false) : base("Foldout")
    {
        Title = title;
        IsExpanded = expanded;
        AddClass("foldout");
        
        CreateLayout();
    }
    
    private void CreateLayout()
    {
        var mainContainer = new VBox();
        AddChild(mainContainer);
        
        // Заголовок с кнопкой переключения
        var headerContainer = new HBox { Gap = 8f };
        headerContainer.AddClass("foldout-header");
        headerContainer.Style.Padding = new Padding(4);
        headerContainer.Style.BackgroundColor = new Color(240, 240, 240, 255);
        
        _toggleButton = new Button(IsExpanded ? "▼" : "▶");
        _toggleButton.AddClass("foldout-toggle");
        _toggleButton.Style.Width = 20;
        _toggleButton.Style.Height = 20;
        _toggleButton.OnClick += Toggle;
        
        var titleLabel = new Label(Title);
        titleLabel.AddClass("foldout-title");
        // titleLabel.Style.FontWeight = FontWeight.Bold;
        
        headerContainer.AddChild(_toggleButton);
        headerContainer.AddChild(titleLabel);
        mainContainer.AddChild(headerContainer);
        
        // Контейнер для содержимого
        _contentContainer = new VisualElement("FoldoutContent");
        _contentContainer.AddClass("foldout-content");
        _contentContainer.Style.Padding = new Padding(8);
        _contentContainer.Visible = IsExpanded;
        
        if (AnimateToggle)
        {
            _currentHeight = IsExpanded ? _contentContainer.Size.Y : 0f;
            _targetHeight = _currentHeight;
        }
        
        mainContainer.AddChild(_contentContainer);
    }
    
    /// <summary>
    /// Добавляет элемент в содержимое foldout
    /// </summary>
    public void AddContent(VisualElement element)
    {
        _contentContainer?.AddChild(element);
        UpdateTargetHeight();
    }
    
    /// <summary>
    /// Удаляет элемент из содержимого foldout
    /// </summary>
    public void RemoveContent(VisualElement element)
    {
        _contentContainer?.RemoveChild(element);
        UpdateTargetHeight();
    }
    
    /// <summary>
    /// Переключает состояние развернутости
    /// </summary>
    public void Toggle()
    {
        SetExpanded(!IsExpanded);
    }
    
    /// <summary>
    /// Программно устанавливает состояние развернутости
    /// </summary>
    public void SetExpanded(bool expanded)
    {
        if (IsExpanded == expanded) return;
        
        IsExpanded = expanded;
        
        if (_toggleButton != null)
            _toggleButton.Text = IsExpanded ? "▼" : "▶";
        
        if (AnimateToggle)
        {
            UpdateTargetHeight();
        }
        else
        {
            if (_contentContainer != null)
                _contentContainer.Visible = IsExpanded;
        }
        
        OnToggle?.Invoke(IsExpanded);
    }
    
    private void UpdateTargetHeight()
    {
        if (_contentContainer == null || !AnimateToggle) return;
        
        if (IsExpanded)
        {
            // Временно делаем видимым для измерения размера
            var wasVisible = _contentContainer.Visible;
            _contentContainer.Visible = true;
            // _contentContainer.AutoResizeToFitChildren();
            _targetHeight = _contentContainer.Size.Y;
            _contentContainer.Visible = wasVisible;
        }
        else
        {
            _targetHeight = 0f;
        }
    }
    
    public override void Update(double deltaTime)
    {
        base.Update(deltaTime);
        
        if (AnimateToggle && _contentContainer != null)
        {
            // Анимация высоты
            if (Math.Abs(_currentHeight - _targetHeight) > 0.1f)
            {
                _currentHeight = MathHelper.Lerp(_currentHeight, _targetHeight, _animationSpeed * (float)deltaTime);
                
                // Обновляем видимость и размер контейнера содержимого
                _contentContainer.Visible = _currentHeight > 0.1f;
                if (_contentContainer.Visible)
                {
                    _contentContainer.Size = new Vector2(_contentContainer.Size.X, _currentHeight);
                }
            }
            else
            {
                _currentHeight = _targetHeight;
                _contentContainer.Visible = IsExpanded;
                if (IsExpanded)
                {
                    // _contentContainer.AutoResizeToFitChildren();
                }
            }
        }
    }
}

/// <summary>
/// Вспомогательный класс для математических операций
/// </summary>
public static class MathHelper
{
    public static float Lerp(float a, float b, float t)
    {
        return a + (b - a) * Math.Max(0f, Math.Min(1f, t));
    }
}