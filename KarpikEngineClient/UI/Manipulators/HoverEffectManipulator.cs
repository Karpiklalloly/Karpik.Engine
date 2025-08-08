using Karpik.Engine.Client.UIToolkit;
using Raylib_cs;

namespace Karpik.Engine.Client.UIToolkit.Manipulators;

public class HoverEffectManipulator : Manipulator
{
    protected override void OnAttach()
    {
        
    }
    
    public override void Update(float deltaTime)
    {
        if (Element == null || !Element.Enabled) return;
        
        var mousePos = Raylib.GetMousePosition();
        Element.HandleHover(Element.ContainsPoint(mousePos));
    }
}