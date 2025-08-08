using Raylib_cs;

namespace Karpik.Engine.Client.UIToolkit.Elements;

public class Label : VisualElement
{
    public string Text { get; set; }
    public TextAlign Alignment { get; set; } = TextAlign.Left;
    
    public Label(string text = "") : base("Label")
    {
        Text = text;
        AddClass("label");
    }
    
    protected override void RenderSelf()
    {
        base.RenderSelf();
        
        if (!string.IsNullOrEmpty(Text))
        {
            var textSize = Raylib.MeasureText(Text, ResolvedStyle.FontSize);
            var textPos = CalculateTextPosition(textSize);
            Raylib.DrawText(Text, (int)textPos.X, (int)textPos.Y, ResolvedStyle.FontSize, ResolvedStyle.TextColor);
        }
    }
    
    private System.Numerics.Vector2 CalculateTextPosition(int textWidth)
    {
        var x = Alignment switch
        {
            TextAlign.Left => Position.X + ResolvedStyle.Padding.Left,
            TextAlign.Center => Position.X + (Size.X - textWidth) / 2,
            TextAlign.Right => Position.X + Size.X - textWidth - ResolvedStyle.Padding.Right,
            _ => Position.X + ResolvedStyle.Padding.Left
        };
        
        var y = Position.Y + (Size.Y - ResolvedStyle.FontSize) / 2;
        
        return new System.Numerics.Vector2(x, y);
    }
}

public enum TextAlign
{
    Left,
    Center,
    Right
}