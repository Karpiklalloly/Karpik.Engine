using System.Numerics;
using Karpik.Engine.Client.UIToolkit.Manipulators;
using Raylib_cs;

namespace Karpik.Engine.Client.UIToolkit;

public class Checkbox : VisualElement
{
    public bool IsChecked { get; set; } = false;
    public string Label { get; set; } = "";
    
    public event Action<bool>? OnCheckedChanged;
    
    private const float CheckboxSize = 20f;
    private const float LabelSpacing = 8f;
    
    public Checkbox(string label = "") : base("Checkbox")
    {
        Label = label;
        AddClass("checkbox");
        
        var clickable = new ClickableManipulator();
        clickable.OnClicked += ToggleChecked;
        AddManipulator(clickable);
        AddManipulator(new HoverEffectManipulator());
    }
    
    private void ToggleChecked()
    {
        if (!Enabled) return;
        
        IsChecked = !IsChecked;
        OnCheckedChanged?.Invoke(IsChecked);
    }
    
    protected override void RenderSelf()
    {
        // Не рендерим базовый фон, рендерим свой
        var checkboxRect = new Rectangle(Position.X, Position.Y + (Size.Y - CheckboxSize) / 2, CheckboxSize, CheckboxSize);
        
        // Фон чекбокса
        var bgColor = IsChecked ? new Color(33, 150, 243, 255) : Color.White;
        if (!Enabled)
            bgColor = new Color(240, 240, 240, 255);
        else if (IsHovered)
            bgColor = IsChecked ? new Color(25, 118, 210, 255) : new Color(245, 245, 245, 255);
            
        Raylib.DrawRectangleRec(checkboxRect, bgColor);
        
        // Рамка чекбокса
        var borderColor = IsChecked ? new Color(33, 150, 243, 255) : new Color(200, 200, 200, 255);
        if (!Enabled)
            borderColor = new Color(200, 200, 200, 255);
            
        Raylib.DrawRectangleLinesEx(checkboxRect, 2f, borderColor);
        
        // Галочка
        if (IsChecked)
        {
            var checkColor = Enabled ? Color.White : new Color(150, 150, 150, 255);
            var centerX = checkboxRect.X + checkboxRect.Width / 2;
            var centerY = checkboxRect.Y + checkboxRect.Height / 2;
            
            // Рисуем галочку как две линии
            var checkSize = CheckboxSize * 0.3f;
            Raylib.DrawLineEx(
                new Vector2(centerX - checkSize * 0.5f, centerY),
                new Vector2(centerX - checkSize * 0.1f, centerY + checkSize * 0.4f),
                2f, checkColor
            );
            Raylib.DrawLineEx(
                new Vector2(centerX - checkSize * 0.1f, centerY + checkSize * 0.4f),
                new Vector2(centerX + checkSize * 0.5f, centerY - checkSize * 0.4f),
                2f, checkColor
            );
        }
        
        // Текст лейбла
        if (!string.IsNullOrEmpty(Label))
        {
            var textX = Position.X + CheckboxSize + LabelSpacing;
            var textY = Position.Y + (Size.Y - ResolvedStyle.FontSize) / 2;
            var textColor = Enabled ? ResolvedStyle.TextColor : new Color(150, 150, 150, 255);
            
            Raylib.DrawText(Label, (int)textX, (int)textY, ResolvedStyle.FontSize, textColor);
        }
    }
}