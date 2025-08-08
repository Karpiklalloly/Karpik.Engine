using Karpik.Engine.Client.UIToolkit.Core;
using Raylib_cs;

namespace Karpik.Engine.Client.UIToolkit.Manipulators;

public class HoverEffectManipulator : Manipulator
{
    public Color HoverColor { get; set; } = Color.LightGray;
    public Color PressedColor { get; set; } = Color.Gray;
    
    private Color _originalColor;
    private bool _hasOriginalColor = false;
    
    protected override void OnAttach()
    {
        if (Element != null)
        {
            _originalColor = Element.Style.BackgroundColor;
            _hasOriginalColor = true;
        }
    }
    
    public override void Update(float deltaTime)
    {
        if (Element == null || !_hasOriginalColor) return;
        
        if (Element.IsPressed)
        {
            Element.Style.BackgroundColor = PressedColor;
        }
        else if (Element.IsHovered)
        {
            Element.Style.BackgroundColor = HoverColor;
        }
        else
        {
            Element.Style.BackgroundColor = _originalColor;
        }
    }
}