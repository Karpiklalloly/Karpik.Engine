namespace Karpik.Engine.Client.Graphics.Core;

public interface ITexture2D : IDisposable
{
    public uint Width { get; }
    public uint Height { get; }
}