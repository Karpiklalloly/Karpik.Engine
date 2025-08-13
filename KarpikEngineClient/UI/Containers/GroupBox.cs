using System.Numerics;
using Raylib_cs;

namespace Karpik.Engine.Client.UIToolkit;

/// <summary>
/// Базовая панель с рамкой для группировки элементов
/// </summary>
public class GroupBox : VisualElement
{
    public string? Title { get; set; }
    public bool Collapsible { get; set; } = false;
    public bool IsCollapsed { get; set; } = false;
    
    private VisualElement? _headerElement;
    private VisualElement? _contentElement;
    private Button? _collapseButton;
    
    public GroupBox(string? title = null) : base("Panel")
    {
        Title = title;
        AddClass("panel");
        
        // Устанавливаем стили по умолчанию
        Style.BorderWidth = 1f;
        Style.BorderColor = new Color(200, 200, 200, 255);
        Style.BackgroundColor = new Color(250, 250, 250, 255);
        Style.Padding = new Padding(8);
        
        CreateLayout();
    }
    
    private void CreateLayout()
    {
        var mainContainer = new VBox();
        AddChild(mainContainer);
        
        // Заголовок (если есть)
        if (!string.IsNullOrEmpty(Title))
        {
            _headerElement = new HBox { Gap = 8f };
            _headerElement.AddClass("panel-header");
            _headerElement.Style.Padding = new Padding(4, 8);
            _headerElement.Style.BackgroundColor = new Color(240, 240, 240, 255);
            
            var titleLabel = new Label(Title);
            titleLabel.AddClass("panel-title");
            // titleLabel.Style.FontWeight = FontWeight.Bold;
            _headerElement.AddChild(titleLabel);
            
            // Кнопка сворачивания если включена
            if (Collapsible)
            {
                _collapseButton = new Button(IsCollapsed ? "▶" : "▼");
                _collapseButton.AddClass("panel-collapse-button");
                _collapseButton.Style.Width = 20;
                _collapseButton.Style.Height = 20;
                _collapseButton.OnClick += ToggleCollapse;
                _headerElement.AddChild(_collapseButton);
            }
            
            mainContainer.AddChild(_headerElement);
        }
        
        // Контейнер для содержимого
        _contentElement = new VisualElement("PanelContent");
        _contentElement.AddClass("panel-content");
        _contentElement.Visible = !IsCollapsed;
        mainContainer.AddChild(_contentElement);
    }
    
    /// <summary>
    /// Добавляет элемент в содержимое панели
    /// </summary>
    public void AddContent(VisualElement element)
    {
        _contentElement?.AddChild(element);
    }
    
    /// <summary>
    /// Удаляет элемент из содержимого панели
    /// </summary>
    public void RemoveContent(VisualElement element)
    {
        _contentElement?.RemoveChild(element);
    }
    
    private void ToggleCollapse()
    {
        IsCollapsed = !IsCollapsed;
        
        if (_contentElement != null)
            _contentElement.Visible = !IsCollapsed;
            
        if (_collapseButton != null)
            _collapseButton.Text = IsCollapsed ? "▶" : "▼";
    }
    
    /// <summary>
    /// Программно сворачивает или разворачивает панель
    /// </summary>
    public void SetCollapsed(bool collapsed)
    {
        if (IsCollapsed != collapsed)
            ToggleCollapse();
    }
}