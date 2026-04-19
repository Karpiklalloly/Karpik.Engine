using System.Numerics;
using Veldrid;

namespace Karpik.Engine.Client.Graphics.Core;

public struct Vertex2D
{
    public Vector2 Position;
    public Vector2 TexCoord;
    public Vector4 Color;

    public static readonly VertexLayoutDescription Layout = new VertexLayoutDescription(
        new VertexElementDescription("Position", VertexElementSemantic.Position, VertexElementFormat.Float2),
        new VertexElementDescription("TexCoord", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float2),
        new VertexElementDescription("Color",    VertexElementSemantic.Color, VertexElementFormat.Float4)
    );
}