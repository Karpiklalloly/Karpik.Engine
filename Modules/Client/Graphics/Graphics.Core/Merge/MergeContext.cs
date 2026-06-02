using Veldrid;

namespace Karpik.Engine.Client.Graphics.Core;

public struct MergeContext
{
    public DeviceBuffer VertexBuffer;
    public Vertex2D[] Vertices;
    public TextGlyphQuad[] TextGlyphs;
    public CommandList CommandList;
}
