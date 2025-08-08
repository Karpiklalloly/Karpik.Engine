using System.Numerics;
using Raylib_cs;

namespace Karpik.Engine.Client.UIToolkit.Elements;

public class ProgressBar : VisualElement
{
    public float Value { get; set; } = 0f;
    public float MinValue { get; set; } = 0f;
    public float MaxValue { get; set; } = 100f;
    public string? Text { get; set; }
    public bool ShowPercentage { get; set; } = true;
    
    public ProgressBar(float minValue = 0f, float maxValue = 100f, float initialValue = 0f) : base("ProgressBar")
    {
        MinValue = minValue;
        MaxValue = maxValue;
        Value = Math.Clamp(initialValue, minValue, maxValue);
        
        AddClass("progressbar");
    }
    
    protected override void RenderSelf()
    {
        // Фон прогресс-бара
        var bgColor = new Color(240, 240, 240, 255);
        Raylib.DrawRectangleRounded(GetBounds(), 0.3f, 8, bgColor);
        
        // Рамка
        var borderColor = new Color(200, 200, 200, 255);
        Raylib.DrawRectangleLinesEx(GetBounds(), 1f, borderColor);
        
        // Заполненная часть
        if (Value > MinValue)
        {
            var percentage = (Value - MinValue) / (MaxValue - MinValue);
            var fillWidth = (Size.X - 4) * percentage; // -4 для отступов от рамки
            
            if (fillWidth > 0)
            {
                var fillRect = new Rectangle(Position.X + 2, Position.Y + 2, fillWidth, Size.Y - 4);
                var fillColor = new Color(76, 175, 80, 255); // Зеленый
                
                Raylib.DrawRectangleRounded(fillRect, 0.3f, 8, fillColor);
            }
        }
        
        // Текст
        var displayText = Text;
        if (string.IsNullOrEmpty(displayText) && ShowPercentage)
        {
            var percentage = (Value - MinValue) / (MaxValue - MinValue) * 100f;
            displayText = $"{percentage:F0}%";
        }
        
        if (!string.IsNullOrEmpty(displayText))
        {
            var textSize = Raylib.MeasureText(displayText, ResolvedStyle.FontSize);
            var textPos = new Vector2(
                Position.X + (Size.X - textSize) / 2,
                Position.Y + (Size.Y - ResolvedStyle.FontSize) / 2
            );
            
            // Используем контрастный цвет для текста
            var textColor = ResolvedStyle.TextColor;
            Raylib.DrawText(displayText, (int)textPos.X, (int)textPos.Y, ResolvedStyle.FontSize, textColor);
        }
    }
    
    public void SetProgress(float value)
    {
        Value = Math.Clamp(value, MinValue, MaxValue);
    }
    
    public float GetPercentage()
    {
        return (Value - MinValue) / (MaxValue - MinValue) * 100f;
    }
}