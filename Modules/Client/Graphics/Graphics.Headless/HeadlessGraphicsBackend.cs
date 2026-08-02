using Karpik.Engine.Client.Graphics.Core;

namespace Karpik.Engine.Client.Graphics.Headless;

public sealed class HeadlessGraphicsBackend : IGraphicsBackend
{
    public bool IsHeadless => true;

    public void Initialize()
    {
    }

    public void BeginFrame()
    {
        GraphicsContext.BeginFrame();
    }

    public void BeginMerge(in Camera2D camera)
    {
    }

    public void SubmitScene()
    {
    }

    public void UpdateImGui(out bool wantsMouse, out bool wantsKeyboard, out bool wantsText)
    {
        wantsMouse = false;
        wantsKeyboard = false;
        wantsText = false;
    }

    public void RenderImGui()
    {
    }

    public void SwapBuffers()
    {
    }

    public void Dispose()
    {
    }
}
