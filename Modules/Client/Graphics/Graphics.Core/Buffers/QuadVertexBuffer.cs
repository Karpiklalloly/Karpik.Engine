using Veldrid;

namespace Karpik.Engine.Client.Graphics.Core;

public static class QuadVertexBuffer
{
    private static readonly float[] Vertices =
    [
        // X, Y          U, V          R, G, B, A
        -1f,  -1f,      0f, 0f,       1f, 1f, 1f, 1f,
        1f,  -1f,      1f, 0f,       1f, 1f, 1f, 1f,
        -1f,   1f,      0f, 1f,       1f, 1f, 1f, 1f,
        1f,   1f,      1f, 1f,       1f, 1f, 1f, 1f
    ];
    
    public static DeviceBuffer Create(GraphicsDevice device)
    {
        return device.ResourceFactory.CreateBuffer(
            new BufferDescription((uint)(Vertices.Length * sizeof(float)), BufferUsage.VertexBuffer));
    }

    public static void Update(GraphicsDevice device, DeviceBuffer buffer)
    {
        device.UpdateBuffer(buffer, 0, Vertices);
    }
}