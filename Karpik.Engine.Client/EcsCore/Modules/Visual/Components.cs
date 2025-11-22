using Karpik.Engine.Shared;
using Raylib_cs;

namespace Karpik.Engine.Client;

[Serializable]
public struct SpriteRenderer : IEcsComponent, IEcsComponentOnLoad
{
    [JsonIgnore] public Texture2D Texture;
    public Color Color;
    public int Layer;
    public string TexturePath;
    public void OnLoad(Loader loader)
    {
        Texture = loader.Load<Texture2D>(TexturePath);
    }
}