namespace Karpik.Engine.Modules.Window.Core;

public class InputCaptureState
{
    public bool Mouse { get; private set; }
    public bool Keyboard { get; private set; }
    public bool Text { get; private set; }

    public void Set(bool mouse, bool keyboard, bool text)
    {
        Mouse = mouse;
        Keyboard = keyboard;
        Text = text;
    }

    public void Clear()
    {
        Mouse = false;
        Keyboard = false;
        Text = false;
    }
}
