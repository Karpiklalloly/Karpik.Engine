using System.Numerics;
using Raylib_cs;

namespace Karpik.Engine.Client.UIToolkit;

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
        
        // Устанавливаем базовые стили
        Style.FontSize = 12;
        Style.Padding = new Padding(8, 6);
        Style.BackgroundColor = new Color(50, 50, 50, 240);
        Style.TextColor = Color.White;
        
        // Важно: устанавливаем абсолютное позиционирование
        Style.Position = Karpik.Engine.Client.UIToolkit.Position.Absolute;
        Style.FlexShrink = 0;
        Style.FlexGrow = 0;
        
        // Рассчитываем размер сразу
        CalculateSize();
    }
    
    public void Show(Vector2 position)
    {
        // Сначала рассчитываем размер
        CalculateSize();
        
        Position = position + Offset;
        
        // Устанавливаем позицию через стили для абсолютного позиционирования
        Style.Left = Position.X;
        Style.Top = Position.Y;
        Style.Position = Karpik.Engine.Client.UIToolkit.Position.Absolute;
        
        // Проверяем границы экрана
        var screenWidth = Raylib.GetRenderWidth();
        var screenHeight = Raylib.GetRenderHeight();
        
        if (Position.X + Size.X > screenWidth)
        {
            Position = new Vector2(position.X - Size.X - Math.Abs(Offset.X), Position.Y);
            Style.Left = Position.X;
        }
        
        if (Position.Y < 0)
        {
            Position = new Vector2(Position.X, position.Y + Math.Abs(Offset.Y) + 20);
            Style.Top = Position.Y;
        }
        
        Visible = true;
        _isShowing = true;
        
        // Убираем анимацию для стабильности
        // FadeIn(0.2f);
    }
    
    public void Hide()
    {
        if (!_isShowing) return;
        
        _isShowing = false;
        _showTimer = 0f;
        Visible = false;
        
        // Убираем анимацию, чтобы избежать проблем с состоянием
        // FadeOut(0.1f, () => Visible = false);
    }
    
    public void UpdatePosition(Vector2 mousePosition)
    {
        if (!_isShowing) return;
        
        Position = mousePosition + Offset;
        
        // Устанавливаем позицию через стили для абсолютного позиционирования
        Style.Left = Position.X;
        Style.Top = Position.Y;
        
        // Проверяем границы экрана
        var screenWidth = Raylib.GetRenderWidth();
        var screenHeight = Raylib.GetRenderHeight();
        
        if (Position.X + Size.X > screenWidth)
        {
            Position = new Vector2(mousePosition.X - Size.X - Math.Abs(Offset.X), Position.Y);
            Style.Left = Position.X;
        }
        
        if (Position.Y < 0)
        {
            Position = new Vector2(Position.X, mousePosition.Y + Math.Abs(Offset.Y) + 20);
            Style.Top = Position.Y;
        }
        
        if (Position.Y + Size.Y > screenHeight)
        {
            Position = new Vector2(Position.X, mousePosition.Y - Size.Y - Math.Abs(Offset.Y));
            Style.Top = Position.Y;
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
            
            Raylib.DrawText(Text, (int)textPos.X, (int)textPos.Y, Style.GetFontSizeOrDefault(), Color.White);
        }
    }
    
    public void CalculateSize()
    {
        if (string.IsNullOrEmpty(Text))
        {
            Size = new Vector2(0, 0);
            Style.Width = 0;
            Style.Height = 0;
            return;
        }
        
        var textWidth = Raylib.MeasureText(Text, Style.GetFontSizeOrDefault());
        Size = new Vector2(
            textWidth + Style.Padding.Left + Style.Padding.Right,
            Style.GetFontSizeOrDefault() + Style.Padding.Top + Style.Padding.Bottom
        );
        
        // Принудительно устанавливаем размер в стилях, чтобы LayoutEngine не переопределил его
        Style.Width = Size.X;
        Style.Height = Size.Y;
        Style.Position = Karpik.Engine.Client.UIToolkit.Position.Absolute;
    }
}

// Манипулятор для добавления tooltip к элементам


public class TooltipManager
{
    private readonly LayerManager _layerManager;
    private readonly Dictionary<Tooltip, string> _tooltipLayers = new();
    
    public TooltipManager(LayerManager layerManager)
    {
        _layerManager = layerManager;
    }
    
    public void ShowTooltip(Tooltip tooltip, Vector2 position)
    {
        var layerName = $"tooltip_{tooltip.GetHashCode()}";
        
        // Если tooltip уже показан, просто обновляем позицию
        if (_tooltipLayers.ContainsKey(tooltip))
        {
            // Проверяем, что слой все еще существует
            var existingLayer = _layerManager.GetLayer(layerName);
            if (existingLayer != null && tooltip.Visible)
            {
                tooltip.UpdatePosition(position);
                return;
            }
            else
            {
                // Слой был удален или tooltip скрыт, удаляем из отслеживания
                _tooltipLayers.Remove(tooltip);
            }
        }
        
        // Удаляем старый слой если он существует
        if (_layerManager.GetLayer(layerName) != null)
        {
            _layerManager.RemoveLayer(layerName);
        }
        
        var layer = _layerManager.CreateLayer(layerName, 3000); // Очень высокий Z-индекс
        layer.AddElement(tooltip); // Добавляем tooltip как дочерний элемент root'а
        layer.BlocksInput = false;
        
        tooltip.Show(position);
        _tooltipLayers[tooltip] = layerName;
    }
    
    public void HideTooltip(Tooltip tooltip)
    {
        if (_tooltipLayers.TryGetValue(tooltip, out var layerName))
        {
            tooltip.Hide();
            _layerManager.RemoveLayer(layerName);
            _tooltipLayers.Remove(tooltip);
        }
    }
    
    public void HideAllTooltips()
    {
        var tooltipsToHide = _tooltipLayers.Keys.ToList();
        foreach (var tooltip in tooltipsToHide)
        {
            HideTooltip(tooltip);
        }
    }
}