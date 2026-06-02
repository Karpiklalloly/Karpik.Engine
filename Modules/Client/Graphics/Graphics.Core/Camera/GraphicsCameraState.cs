namespace Karpik.Engine.Client.Graphics.Core;

public sealed class GraphicsCameraState
{
    public Camera2D ActiveCamera;
    public Camera2D FrameCamera { get; private set; }

    public void SetActive(in Camera2D camera)
    {
        ActiveCamera = camera;
    }

    public void CaptureForFrame(float framebufferWidth, float framebufferHeight)
    {
        FrameCamera = ActiveCamera.Normalized(framebufferWidth, framebufferHeight);
    }
}
