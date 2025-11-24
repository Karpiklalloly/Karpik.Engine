using Karpik.Engine.Client.AssetManagement.Assets;
using Karpik.Engine.Shared;
using Raylib_cs;

namespace Karpik.Engine.Client;

[Serializable]
public struct SpriteRenderer : IEcsComponent, IEcsComponentOnLoad, IEcsComponentLifeCycle
{
    [JsonIgnore] public Texture2D Texture => _handle.Asset.Texture;
    public Color Color;
    public int Layer;
    public string TexturePath;
    
    private AssetHandle<Texture2DAsset> _handle;
    
    // TODO: есть ли возможность диспоуз из мира вызывать? Если нет, то надо зависимости добавлять. Но тогда и условного врага надо постоянно хранить в общем хз
    public async Task OnLoad(AssetsManager manager)
    {
        _handle.Dispose();
        _handle = await manager.LoadAssetAsync<Texture2DAsset>(TexturePath);
    }

    public void Dispose()
    {
        _handle.Dispose();
    }
}