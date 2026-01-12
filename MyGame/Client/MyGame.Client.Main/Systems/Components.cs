using System.Drawing;
using DCFApixels.DragonECS;
using DCFApixels.DragonECS.Core;
using Karpik.Engine.Client.Graphics.Core;
using Karpik.Engine.Client.Graphics.Core.AssetManagement;
using Karpik.Engine.Shared.AssetManagement.Core;
using Karpik.Engine.Shared.ECS;
using Karpik.Jobs;
using Newtonsoft.Json;

namespace Karpik.Engine.MyGame.Client.Main.Systems;


[Serializable]
public struct SpriteRenderer : IEcsComponent, IHasDependencies, IEcsComponentOnLoad<SpriteRenderer>, IEcsComponentLifecycle<SpriteRenderer>
{
    [JsonIgnore] public ITexture2D Texture => _handle.Asset.Texture;
    [JsonConverter(typeof(ColorConverter))] public Color Color;
    public int Layer;
    public string TexturePath;
    
    private AssetHandle<Texture2DAsset> _handle;
    
    public async JobHandle<SpriteRenderer> OnLoad(SpriteRenderer renderer, IAssetsManager manager)
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