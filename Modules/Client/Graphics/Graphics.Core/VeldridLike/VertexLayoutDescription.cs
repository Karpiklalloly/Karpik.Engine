namespace Karpik.Engine.Client.Graphics.Core.VeldridLike;

public readonly struct VertexLayoutDescription
{
    public readonly VertexElementDescription[] Elements;
    public readonly uint Stride;

    public VertexLayoutDescription(uint stride, params VertexElementDescription[] elements)
    {
        Stride = stride; Elements = elements;
    }
}