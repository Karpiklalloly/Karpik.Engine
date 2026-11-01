using NeoVeldrid;
using Karpik.Content.Runtime;
namespace Karpik.Engine.Client.Graphics.Core;

public sealed class VeldridTexture2D : ITexture2D, IContentTrackedResource
{
    public readonly Texture Texture;
    public readonly ResourceSet ResourceSet;
    public uint Width { get; }
    public uint Height { get; }
    public ContentResourceLifetime? Lifetime { get; }
    private int _disposed;
    /// <summary>Legacy constructor: only texture ownership is transferred.</summary>
    public VeldridTexture2D(Texture texture, ResourceSet resourceSet)
    {
        Texture = texture; ResourceSet = resourceSet; Width = texture.Width; Height = texture.Height;
    }
    /// <summary>Content owns the complete bundle through the supplied ordered backend destruction callback.</summary>
    public VeldridTexture2D(Texture texture, ResourceSet resourceSet, ContentResourceLifetime lifetime) : this(texture, resourceSet)
        => Lifetime = lifetime;
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        if (Lifetime == null) Texture.Dispose();
        else { Lifetime.Retire(); _ = Task.Run(Lifetime.DrainAsync); }
    }
}
