using System.Numerics;

namespace Karpik.Engine.Client.UIToolkit;

/// <summary>
/// Горизонтальный контейнер для размещения элементов в ряд
/// </summary>
public class HBox : VisualElement
{
    public float Gap { get; set; } = 0f;
    
    public HBox() : base("HBox")
    {
        AddClass("hbox");
    }
    
    public override void Update(double deltaTime)
    {
        base.Update(deltaTime);
        ArrangeChildren();
    }
    
    private void ArrangeChildren()
    {
        if (Children.Count == 0) return;
        
        var currentX = Position.X + (ResolvedStyle.Padding?.Left ?? 0);
        var containerY = Position.Y + (ResolvedStyle.Padding?.Top ?? 0);
        
        foreach (var child in Children)
        {
            if (!child.Visible || child.IgnoreLayout) continue;
            
            var childMargin = child.ResolvedStyle.Margin;
            currentX += childMargin?.Left ?? 0;
            
            child.Position = new Vector2(currentX, containerY + (childMargin?.Top ?? 0));
            
            currentX += child.Size.X + (childMargin?.Right ?? 0) + Gap;
        }
        
        // Автоматически подгоняем размер контейнера
        // AutoResizeToFitChildren();
    }
}