using System.Composition;
using System.Text;
using Karpik.Engine.Core;
using Karpik.Engine.Shared.AssetManagement.Core;
using Karpik.Jobs;

namespace Karpik.Engine.Client.Graphics.Core.AssetManagement;

[Export(typeof(IAssetLoader))]
[ServiceRegistration(ModuleScope.Engine, ServiceLifetime.Singleton)]
public class ShaderLoader : BaseAssetLoader<ShaderAsset, byte[]>
{
    public override string? DefaultPath => null;
    public override string[] SupportedExtensions => [".vert", ".frag"];
    protected override JobHandle<byte[]?> OnLoadAsync(IAssetLoadContext context, Stream stream, string assetName)
    {
        return Job.Run<byte[]?>(() =>
        {
            using var reader = new StreamReader(stream, leaveOpen: true);
            return Encoding.UTF8.GetBytes(reader.ReadToEnd());
        });
    }

    protected override ShaderAsset EmptyAsset() => new();

    protected override void SetValue(IAssetLoadContext context, ShaderAsset asset, byte[] value)
    {
        asset.ShaderBytes = value;
    }
}