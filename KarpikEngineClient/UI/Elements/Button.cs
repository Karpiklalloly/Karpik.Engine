using Karpik.Engine.Client.UIToolkit;
using Karpik.Engine.Client.UIToolkit.Manipulators;
using Raylib_cs;

namespace Karpik.Engine.Client.UIToolkit;

public class Button : VisualElement, ITextProvider
{
    public string Text { get; set; }
    public event Action OnClick;
    
    public Button(string text = "Button") : base("Button")
    {
        Text = text;
        
        // Добавляем класс по умолчанию
        AddClass("button");
        
        // Добавляем базовые манипуляторы
        var clickable = new ClickableManipulator();
        clickable.OnClicked += () => OnClick?.Invoke();
        AddManipulator(clickable);
        AddManipulator(new HoverEffectManipulator());
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