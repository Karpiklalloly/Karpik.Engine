using Veldrid;

using Karpik.Engine.Client.Graphics.Core;

namespace Karpik.Engine.Client.Graphics.OpenGL;

public struct MergeContext
{
    public DeviceBuffer VertexBuffer;
    public Vertex2D[] Vertices;
    public TextGlyphQuad[] TextGlyphs;
    public CommandList CommandList;
    public Fence SubmitFence;
}
