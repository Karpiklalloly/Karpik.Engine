using System.Numerics;
using Raylib_cs;

namespace Karpik.Engine.Client.UIToolkit.Elements;

public class Tooltip : VisualElement
{
    public string Text { get; set; }
    public float ShowDelay { get; set; } = 0.5f; // Задержка перед показом
    public Vector2 Offset { get; set; } = new(10, -30); // Смещение относительно курсора
    
    private float _showTimer = 0f;
    private bool _isShowing = false;
    
    public Tooltip(string text) : base("Tooltip")
    {
        Text = text;
        AddClass("tooltip");
        Visible = false;
    }
    
    public void Show(Vector2 position)
    {
        Position = position + Offset;
        
        // Проверяем границы экрана
        var screenWidth = Raylib.GetRenderWidth();
        var screenHeight = Raylib.GetRenderHeight();
        
        if (Position.X + Size.X > screenWidth)
        {
            Position = new Vector2(position.X - Size.X - Math.Abs(Offset.X), Position.Y);
        }
        
        if (Position.Y < 0)
        {
            Position = new Vector2(Position.X, position.Y + Math.Abs(Offset.Y) + 20);
        }
        
        Visible = true;
        _isShowing = true;
        
        // Анимация появления
        FadeIn(0.2f);
    }
    
    public void Hide()
    {
        if (!_isShowing) return;
        
        _isShowing = false;
        _showTimer = 0f;
        
        FadeOut(0.1f, () => Visible = false);
    }
    
    public void UpdatePosition(Vector2 mousePosition)
    {
        if (!_isShowing) return;
        
        Position = mousePosition + Offset;
        
        // Проверяем границы экрана
        var screenWidth = Raylib.GetRenderWidth();
        var screenHeight = Raylib.GetRenderHeight();
        
        if (Position.X + Size.X > screenWidth)
        {
            Position = new Vector2(mousePosition.X - Size.X - Math.Abs(Offset.X), Position.Y);
        }
        
        if (Position.Y < 0)
        {
            Position = new Vector2(Position.X, mousePosition.Y + Math.Abs(Offset.Y) + 20);
        }
    }
    
    protected override void RenderSelf()
    {
        if (!Visible) return;
        
        // Рендерим тень
        var shadowOffset = new Vector2(2, 2);
        var shadowColor = new Color(0, 0, 0, 100);
        Raylib.DrawRectangle(
            (int)(Position.X + shadowOffset.X), 
            (int)(Position.Y + shadowOffset.Y),
            (int)Size.X, (int)Size.Y, 
            shadowColor
        );
        
        // Рендерим фон
        Raylib.DrawRectangle((int)Position.X, (int)Position.Y, (int)Size.X, (int)Size.Y, 
            new Color(50, 50, 50, 240));
        
        // Рендерим текст
        if (!string.IsNullOrEmpty(Text))
        {
            var textPos = new Vector2(
                Position.X + Style.Padding.Left,
                Position.Y + Style.Padding.Top
            );
            
            Raylib.DrawText(Text, (int)textPos.X, (int)textPos.Y, Style.FontSize, Color.White);
        }
    }
    
    public void CalculateSize()
    {
        if (string.IsNullOrEmpty(Text))
        {
            Size = new Vector2(0, 0);
            return;
        }
        
        var textWidth = Raylib.MeasureText(Text, Style.FontSize);
        Size = new Vector2(
            textWidth + Style.Padding.Left + Style.Padding.Right,
            Style.FontSize + Style.Padding.Top + Style.Padding.Bottom
        );
    }
}

// Манипулятор для добавления tooltip к элементам
public class TooltipManipulator : IManipulator
{
    private VisualElement? _element;
    private Tooltip? _tooltip;
    private float _hoverTimer = 0f;
    private bool _isHovering = false;
    private readonly string _tooltipText;
    private readonly float _showDelay;
    
    public TooltipManipulator(string tooltipText, float showDelay = 0.5f)
    {
        _tooltipText = tooltipText;
        _showDelay = showDelay;
    }
    
    public void Attach(VisualElement element)
    {
        _element = element;
        _tooltip = new Tooltip(_tooltipText) { ShowDelay = _showDelay };
        _tooltip.CalculateSize();
    }
    
    public void Detach(VisualElement element)
    {
        _tooltip?.Hide();
        _element = null;
        _tooltip = null;
        _isHovering = false;
        _hoverTimer = 0f;
    }
    
    public void Update(float deltaTime)
    {
        if (_element == null || _tooltip == null) return;
        
        var mousePos = Raylib.GetMousePosition();
        var isCurrentlyHovering = _element.ContainsPoint(mousePos);
        
        if (isCurrentlyHovering && !_isHovering)
        {
            // Начинаем наведение
            _isHovering = true;
            _hoverTimer = 0f;
        }
        else if (!isCurrentlyHovering && _isHovering)
        {
            // Заканчиваем наведение
            _isHovering = false;
            _hoverTimer = 0f;
            _tooltip.Hide();
        }
        
        if (_isHovering)
        {
            _hoverTimer += deltaTime;
            
            if (_hoverTimer >= _showDelay && !_tooltip.Visible)
            {
                _tooltip.Show(mousePos);
            }
            else if (_tooltip.Visible)
            {
                _tooltip.UpdatePosition(mousePos);
            }
        }
    }
}

public class TooltipManager
{
    private readonly LayerManager _layerManager;
    private readonly List<Tooltip> _activeTooltips = new();
    
    public TooltipManager(LayerManager layerManager)
    {
        _layerManager = layerManager;
    }
    
    public void ShowTooltip(Tooltip tooltip, Vector2 position)
    {
        var layerName = $"tooltip_{DateTime.Now.Ticks}";
        var layer = _layerManager.CreateLayer(layerName, 3000); // Очень высокий Z-индекс
        
        layer.Root = tooltip;
        layer.BlocksInput = false;
        
        tooltip.Show(position);
        _activeTooltips.Add(tooltip);
    }
    
    public void HideTooltip(Tooltip tooltip)
    {
        _activeTooltips.Remove(tooltip);
        
        var layer = _layerManager.Layers.FirstOrDefault(l => l.Root == tooltip);
        if (layer != null)
        {
            tooltip.Hide();
            _layerManager.RemoveLayer(layer.Name);
        }
    }
    
    public void HideAllTooltips()
    {
        var tooltipsToHide = _activeTooltips.ToList();
        foreach (var tooltip in tooltipsToHide)
        {
            HideTooltip(tooltip);
        }
    }
}