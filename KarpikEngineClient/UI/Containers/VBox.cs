using System.Numerics;

namespace Karpik.Engine.Client.UIToolkit;

/// <summary>
/// Вертикальный контейнер для размещения элементов в колонку
/// </summary>
public class VBox : VisualElement
{
    public float Gap { get; set; } = 0f;
    
    public VBox() : base("VBox")
    {
        AddClass("vbox");
    }
    
    public override void Update(double deltaTime)
    {
        base.Update(deltaTime);
        ArrangeChildren();
    }
    
    private void ArrangeChildren()
    {
        if (Children.Count == 0) return;
        
        var containerX = Position.X + (ResolvedStyle.Padding?.Left ?? 0);
        var currentY = Position.Y + (ResolvedStyle.Padding?.Top ?? 0);
        
        foreach (var child in Children)
        {
            if (!child.Visible || child.IgnoreLayout) continue;
            
            var childMargin = child.ResolvedStyle.Margin;
            currentY += childMargin?.Top ?? 0;
            
            child.Position = new Vector2(containerX + (childMargin?.Left ?? 0), currentY);
            
            currentY += child.Size.Y + (childMargin?.Bottom ?? 0) + Gap;
        }
        
        // Автоматически подгоняем размер контейнера
        // AutoResizeToFitChildren();
    }
}