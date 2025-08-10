using Raylib_cs;

namespace Karpik.Engine.Client.UIToolkit;

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
        
        DrawText(Text, Alignment);
    }
}

public enum TextAlign
{
    Left,
    Center,
    Right
}