using Veldrid;

namespace Karpik.Engine.Client.Graphics.Core;

public sealed class VeldridTexture2D(Texture texture) : ITexture2D, IDisposable
{
    public readonly Texture Texture = texture; 
    
    public void Dispose() => Texture.Dispose();
}