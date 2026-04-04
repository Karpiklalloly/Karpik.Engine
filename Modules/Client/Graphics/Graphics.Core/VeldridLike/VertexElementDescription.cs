namespace Karpik.Engine.Client.Graphics.Core.VeldridLike;

public readonly struct VertexElementDescription
{
    public readonly uint SemanticHash; 
    public readonly PixelFormat Format;
    public readonly uint Offset;

    public VertexElementDescription(uint semanticHash, PixelFormat format, uint offset)
    {
        SemanticHash = semanticHash; Format = format; Offset = offset;
    }
}