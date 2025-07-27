using Karpik.Engine.Shared;
using Raylib_cs;

namespace Karpik.Engine.Client;

[Serializable]
public struct SpriteRenderer : IEcsComponent
{
    [JsonIgnore] public Texture2D Texture;
    public Color Color;
    public int Layer;
    public string TexturePath
    {
        readonly get => _path;
        set
        {
            if (value == _path) return;
            _path = value;
            Texture = Loader.Instance.Load<Texture2D>(value);
        }
    }

    private string _path;
}