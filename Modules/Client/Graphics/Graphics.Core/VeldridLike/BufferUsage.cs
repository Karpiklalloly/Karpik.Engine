namespace Karpik.Engine.Client.Graphics.Core.VeldridLike;

[Flags]
public enum BufferUsage : byte
{
    VertexBuffer = 1 << 0,
    IndexBuffer = 1 << 1,
    UniformBuffer = 1 << 2,
    StructuredBuffer = 1 << 3,
    Staging = 1 << 4
}