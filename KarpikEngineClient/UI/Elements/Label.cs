using Raylib_cs;

namespace Karpik.Engine.Client.UIToolkit;

public class Label : VisualElement, ITextProvider
{
    public string Text { get; set; }
    
    public Label(string text = "") : base("Label")
    {
        Text = text;
        AddClass("label");
    }
    
    // Реализация ITextProvider для LayoutEngine
    public string? GetDisplayText() => Text;
    public IEnumerable<string>? GetTextOptions() => null;
    public string? GetPlaceholderText() => null;
    
    protected override void RenderSelf()
    {
        base.RenderSelf();
        
        DrawText(Text);
    }
}