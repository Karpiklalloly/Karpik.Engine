using DCFApixels.DragonECS.Core;
using Karpik.Engine.Client.AssetManagement.Assets;
using Karpik.Engine.Shared;
using Raylib_cs;

namespace Karpik.Engine.Client;

[Serializable]
public struct SpriteRenderer : IEcsComponent, IEcsComponentOnLoad, IEcsComponentLifecycle<SpriteRenderer>
{
    [JsonIgnore] public Texture2D Texture => _handle.Asset.Texture;
    public Color Color;
    public int Layer;
    public string TexturePath;
    
    private AssetHandle<Texture2DAsset> _handle;
    
    public async Task OnLoad(AssetsManager manager)
    {
        _handle.Dispose();
        _handle = await manager.LoadAssetAsync<Texture2DAsset>(TexturePath);
    }

    public void Enable(ref SpriteRenderer component)
    {

    }

    public void Disable(ref SpriteRenderer component)
    {
        _handle.Dispose();
    }
}