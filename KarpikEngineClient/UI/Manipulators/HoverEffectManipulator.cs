using Raylib_cs;

namespace Karpik.Engine.Client.UIToolkit.Manipulators;

public class HoverEffectManipulator : Manipulator
{
    public override bool Handle(InputEvent inputEvent)
    {
        if (Element == null || !Element.Enabled) return false;
        
        if (inputEvent.Type == InputEventType.MouseMove)
        {
            Element.HandleHover(Element.ContainsPoint(inputEvent.MousePosition));
            return true;
        }

        return false;
    }

    protected override void OnAttach()
    {
        
    }
    
    public override void Update(double deltaTime)
    {
        
    }
}