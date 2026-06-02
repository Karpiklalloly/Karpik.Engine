namespace Karpik.Engine.Client.Graphics.Core;

public sealed class ImGuiOverlayState
{
    public bool Enabled { get; private set; }
    public bool WantsMouseCapture { get; private set; }
    public bool WantsKeyboardCapture { get; private set; }
    public bool WantsTextCapture { get; private set; }

    public void Toggle()
    {
        SetEnabled(!Enabled);
    }

    public void SetEnabled(bool enabled)
    {
        Enabled = enabled;
        if (!Enabled)
        {
            ClearCapture();
        }
    }

    public void SetCapture(bool mouse, bool keyboard, bool text)
    {
        WantsMouseCapture = mouse;
        WantsKeyboardCapture = keyboard;
        WantsTextCapture = text;
    }

    public void ClearCapture()
    {
        WantsMouseCapture = false;
        WantsKeyboardCapture = false;
        WantsTextCapture = false;
    }
}
