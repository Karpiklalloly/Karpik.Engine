namespace Karpik.Engine.Client.Graphics.Core.VeldridLike;

public readonly struct RasterizerStateDescription
{
    public readonly CullMode CullingMode;
    public readonly PolygonMode FillMode;
    public readonly FrontFace FrontFace;
    public readonly bool DepthClipEnabled;

    public RasterizerStateDescription(CullMode cullMode, PolygonMode fillMode, FrontFace frontFace, bool depthClipEnabled)
    {
        CullingMode = cullMode; FillMode = fillMode; 
        FrontFace = frontFace; DepthClipEnabled = depthClipEnabled;
    }

    // Удобный дефолт для стандартного 3D: отсекать задние грани, заливка, по часовой
    public static readonly RasterizerStateDescription Default = 
        new(CullMode.Back, PolygonMode.Fill, FrontFace.Clockwise, true);
    
    // Для 2D UI: не отсекать ничего
    public static readonly RasterizerStateDescription CullNone = 
        new(CullMode.None, PolygonMode.Fill, FrontFace.Clockwise, true);
}