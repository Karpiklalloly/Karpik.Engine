using System.Numerics;
using Raylib_cs;

namespace Karpik.Engine.Client.UIToolkit;

/// <summary>
/// Карточка с тенью и скругленными углами для группировки контента
/// </summary>
public class Card : VisualElement
{
    public string? Title { get; set; }
    public bool ShowShadow { get; set; } = true;
    public float ShadowOffset { get; set; } = 4f;
    public Color ShadowColor { get; set; } = new Color(0, 0, 0, 50);
    
    private VisualElement? _headerElement;
    private VisualElement? _contentElement;
    
    public Card(string? title = null) : base("Card")
    {
        Title = title;
        AddClass("card");
        
        // Устанавливаем стили по умолчанию
        Style.BackgroundColor = Color.White;
        Style.BorderRadius = 8f;
        Style.Padding = new Padding(16);
        
        CreateLayout();
    }
    
    private void CreateLayout()
    {
        // Создаем вертикальный контейнер для заголовка и содержимого
        var mainContainer = new VBox { Gap = 8f };
        AddChild(mainContainer);
        
        // Заголовок (если есть)
        if (!string.IsNullOrEmpty(Title))
        {
            _headerElement = new Label(Title);
            _headerElement.AddClass("card-header");
            _headerElement.Style.FontSize = 18;
            // _headerElement.Style.FontWeight = FontWeight.Bold;
            mainContainer.AddChild(_headerElement);
        }
        
        // Контейнер для содержимого
        _contentElement = new VisualElement("CardContent");
        _contentElement.AddClass("card-content");
        mainContainer.AddChild(_contentElement);
    }
    
    /// <summary>
    /// Добавляет элемент в содержимое карточки
    /// </summary>
    public void AddContent(VisualElement element)
    {
        _contentElement?.AddChild(element);
    }
    
    /// <summary>
    /// Удаляет элемент из содержимого карточки
    /// </summary>
    public void RemoveContent(VisualElement element)
    {
        _contentElement?.RemoveChild(element);
    }
    
    protected override void RenderSelf()
    {
        // Рендерим тень если включена
        if (ShowShadow)
        {
            var shadowBounds = new Rectangle(
                Position.X + ShadowOffset,
                Position.Y + ShadowOffset,
                Size.X,
                Size.Y
            );
            
            var borderRadius = ResolvedStyle.BorderRadius ?? 0;
            if (borderRadius > 0)
            {
                Raylib.DrawRectangleRounded(shadowBounds, Math.Min(borderRadius, 1f), 8, ShadowColor);
            }
            else
            {
                Raylib.DrawRectangle(
                    (int)shadowBounds.X, (int)shadowBounds.Y,
                    (int)shadowBounds.Width, (int)shadowBounds.Height,
                    ShadowColor
                );
            }
        }
        
        // Рендерим саму карточку
        base.RenderSelf();
    }
}