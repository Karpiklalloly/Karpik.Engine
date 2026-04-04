namespace Karpik.Engine.Client.Graphics.Core.VeldridLike;

public readonly struct BufferDescription
{
    public readonly uint SizeInBytes;
    public readonly BufferUsage Usage;

    public BufferDescription(uint sizeInBytes, BufferUsage usage)
    {
        SizeInBytes = sizeInBytes;
        Usage = usage;
    }
}