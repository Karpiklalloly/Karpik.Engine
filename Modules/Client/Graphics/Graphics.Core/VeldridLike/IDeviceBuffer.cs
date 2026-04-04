namespace Karpik.Engine.Client.Graphics.Core.VeldridLike;

public interface IDeviceBuffer : IGraphicsResource
{
    uint SizeInBytes { get; }
    BufferUsage Usage { get; }
}