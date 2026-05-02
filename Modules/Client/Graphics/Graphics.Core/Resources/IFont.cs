namespace Karpik.Engine.Client.Graphics.Core;

public interface IFont : IDisposable
{
    public ITexture2D AtlasTexture { get; }
    public float Size { get; }
    public float LineHeight { get; }
    public float Ascender { get; }
    public float Descender { get; }
    public float DistanceRange { get; }
    public bool TryGetGlyph(uint codepoint, out FontGlyph glyph);
}
