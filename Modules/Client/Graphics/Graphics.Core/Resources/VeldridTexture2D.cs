using Veldrid;

namespace Karpik.Engine.Client.Graphics.Core;

public sealed class VeldridTexture2D(Texture texture, ResourceSet resourceSet) : ITexture2D, IDisposable
{
    public readonly Texture Texture = texture;
    public readonly ResourceSet ResourceSet = resourceSet;
    
    public void Dispose() => Texture.Dispose();
}