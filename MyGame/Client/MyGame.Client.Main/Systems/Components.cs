using System.Drawing;
using DCFApixels.DragonECS;
using DragonExtensions;
using Karpik.Engine.Client.Graphics.Core;
using Karpik.Engine.Client.Graphics.Core.AssetManagement;
using Karpik.Engine.Shared.AssetManagement.Core;
using Karpik.Jobs;
using Newtonsoft.Json;

namespace Karpik.Engine.MyGame.Client.Main.Systems;


[Serializable]
public struct SpriteRenderer : IEcsComponent, IHasDependencies, IComponentLifecycleAsync<SpriteRenderer>
{
    [JsonIgnore] public ITexture2D? Texture => _handle.Asset?.Texture;
    [JsonConverter(typeof(ColorConverter))] public Color Color;
    public int Layer;
    public string TexturePath;
    
    // Size in world units (meters). If 0, uses texture size scaled by default factor
    public float Width;
    public float Height;
    
    private AssetHandle<TextureAsset> _handle;

    public IEnumerable<string> GetDependencyPaths()
    {
        yield return TexturePath;
    }

    public async JobHandle<SpriteRenderer> EnableAsync(SpriteRenderer component, ComponentLifecycleContext context)
    {
        var manager = context.Services.Get<IAssetsManager>();
        component._handle.Dispose();
        component._handle = await manager.LoadAssetAsync<TextureAsset>(component.TexturePath);
        return component;
    }

    public JobHandle<SpriteRenderer> DisableAsync(SpriteRenderer component, ComponentLifecycleContext context)
    {
        component._handle.Dispose();
        return JobHandle<SpriteRenderer>.FromResult(component);
    }
}