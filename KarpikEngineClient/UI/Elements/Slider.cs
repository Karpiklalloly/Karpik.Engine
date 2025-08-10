using System.Numerics;
using Karpik.Engine.Client.UIToolkit.Manipulators;
using Raylib_cs;

namespace Karpik.Engine.Client.UIToolkit;

public class Slider : VisualElement
{
    public float Value { get; set; } = 0f;
    public float MinValue { get; set; } = 0f;
    public float MaxValue { get; set; } = 100f;
    public float Step { get; set; } = 1f;
    
    public event Action<float>? OnValueChanged;
    
    private bool _isDragging = false;
    private const float HandleSize = 20f;
    private const float TrackHeight = 6f;
    
    public Slider(float minValue = 0f, float maxValue = 100f, float initialValue = 0f) : base("Slider")
    {
        MinValue = minValue;
        MaxValue = maxValue;
        Value = Math.Clamp(initialValue, minValue, maxValue);
        
        AddClass("slider");
        AddManipulator(new HoverEffectManipulator());
    }
    
    public override void Update(float deltaTime)
    {
        base.Update(deltaTime);
        
        if (!Enabled) return;
        
        var mousePos = Raylib.GetMousePosition();
        var handleRect = GetHandleRect();
        
        // Начинаем перетаскивание
        if (Raylib.IsMouseButtonPressed(MouseButton.Left))
        {
            if (ContainsPoint(mousePos) || Raylib.CheckCollisionPointRec(mousePos, handleRect))
            {
                _isDragging = true;
                UpdateValueFromMousePosition(mousePos);
            }
        }
        
        // Продолжаем перетаскивание
        if (_isDragging && Raylib.IsMouseButtonDown(MouseButton.Left))
        {
            UpdateValueFromMousePosition(mousePos);
        }
        
        // Заканчиваем перетаскивание
        if (Raylib.IsMouseButtonReleased(MouseButton.Left))
        {
            _isDragging = false;
        }
    }
    
    private void UpdateValueFromMousePosition(Vector2 mousePos)
    {
        var trackRect = GetTrackRect();
        var relativeX = mousePos.X - trackRect.X;
        var percentage = Math.Clamp(relativeX / trackRect.Width, 0f, 1f);
        
        var newValue = MinValue + percentage * (MaxValue - MinValue);
        
        // Применяем шаг
        if (Step > 0)
        {
            newValue = MathF.Round(newValue / Step) * Step;
        }
        
        newValue = Math.Clamp(newValue, MinValue, MaxValue);
        
        if (Math.Abs(Value - newValue) > 0.001f)
        {
            Value = newValue;
            OnValueChanged?.Invoke(Value);
        }
    }
    
    private Rectangle GetTrackRect()
    {
        var trackY = Position.Y + (Size.Y - TrackHeight) / 2;
        return new Rectangle(Position.X + HandleSize / 2, trackY, Size.X - HandleSize, TrackHeight);
    }
    
    private Rectangle GetHandleRect()
    {
        var trackRect = GetTrackRect();
        var percentage = (Value - MinValue) / (MaxValue - MinValue);
        var handleX = trackRect.X + percentage * trackRect.Width - HandleSize / 2;
        var handleY = Position.Y + (Size.Y - HandleSize) / 2;
        
        return new Rectangle(handleX, handleY, HandleSize, HandleSize);
    }
    
    protected override void RenderSelf()
    {
        // Не рендерим базовый фон
        
        var trackRect = GetTrackRect();
        var handleRect = GetHandleRect();
        
        // Рендерим трек
        var trackColor = Enabled ? new Color(200, 200, 200, 255) : new Color(240, 240, 240, 255);
        Raylib.DrawRectangleRounded(trackRect, 0.5f, 8, trackColor);
        
        // Рендерим заполненную часть трека
        if (Value > MinValue)
        {
            var percentage = (Value - MinValue) / (MaxValue - MinValue);
            var filledWidth = trackRect.Width * percentage;
            var filledRect = new Rectangle(trackRect.X, trackRect.Y, filledWidth, trackRect.Height);
            
            var fillColor = Enabled ? new Color(33, 150, 243, 255) : new Color(150, 150, 150, 255);
            Raylib.DrawRectangleRounded(filledRect, 0.5f, 8, fillColor);
        }
        
        // Рендерим ручку
        var handleColor = Color.White;
        if (!Enabled)
            handleColor = new Color(240, 240, 240, 255);
        else if (_isDragging)
            handleColor = new Color(245, 245, 245, 255);
        else if (IsHovered)
            handleColor = new Color(250, 250, 250, 255);
            
        Raylib.DrawCircle((int)(handleRect.X + handleRect.Width / 2), (int)(handleRect.Y + handleRect.Height / 2), HandleSize / 2, handleColor);
        
        // Рамка ручки
        var borderColor = Enabled ? new Color(33, 150, 243, 255) : new Color(200, 200, 200, 255);
        Raylib.DrawCircleLines((int)(handleRect.X + handleRect.Width / 2), (int)(handleRect.Y + handleRect.Height / 2), HandleSize / 2, borderColor);
    }
}