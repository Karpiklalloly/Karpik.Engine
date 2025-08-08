using Karpik.Engine.Client.UIToolkit;
using Karpik.Engine.Client.UIToolkit.Manipulators;
using Raylib_cs;

namespace Karpik.Engine.Client.UIToolkit.Elements;

public class Button : VisualElement
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
    
    protected override void RenderSelf()
    {
        base.RenderSelf();
        
        // Рендерим текст
        if (!string.IsNullOrEmpty(Text))
        {
            var textSize = Raylib.MeasureText(Text, ResolvedStyle.FontSize);
            var textPos = new System.Numerics.Vector2(
                Position.X + (Size.X - textSize) / 2,
                Position.Y + (Size.Y - ResolvedStyle.FontSize) / 2
            );
            Raylib.DrawText(Text, (int)textPos.X, (int)textPos.Y, ResolvedStyle.FontSize, ResolvedStyle.TextColor);
        }
    }
}