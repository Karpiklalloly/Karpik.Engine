using DCFApixels.DragonECS.Core;
using Karpik.Engine.Client.AssetManagement.Assets;
using Karpik.Engine.Shared;
using Raylib_cs;

namespace Karpik.Engine.Client;

[Serializable]
public struct SpriteRenderer : IEcsComponent, IHasDependencies, IEcsComponentOnLoad<SpriteRenderer>, IEcsComponentLifecycle<SpriteRenderer>
{
    [JsonIgnore] public Texture2D Texture => _handle.Asset.Texture;
    public Color Color;
    public int Layer;
    public string TexturePath;
    
    private AssetHandle<Texture2DAsset> _handle;
    
    public async Task<SpriteRenderer> OnLoad(SpriteRenderer renderer, AssetsManager manager)
    {
        renderer._handle.Dispose();
        renderer._handle = await manager.LoadAssetAsync<Texture2DAsset>(TexturePath);
        return renderer;
    }

    public void Enable(ref SpriteRenderer component)
    {

    }

    public void Disable(ref SpriteRenderer component)
    {
        component._handle.Dispose();
    }

    public IEnumerable<string> GetDependencyPaths()
    {
        yield return TexturePath;
    }
}