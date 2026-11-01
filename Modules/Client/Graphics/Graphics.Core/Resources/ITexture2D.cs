namespace Karpik.Engine.Client.Graphics.Core;

[Karpik.Content.Runtime.ContentType("texture")]
public interface ITexture2D : IDisposable
{
    public uint Width { get; }
    public uint Height { get; }
}