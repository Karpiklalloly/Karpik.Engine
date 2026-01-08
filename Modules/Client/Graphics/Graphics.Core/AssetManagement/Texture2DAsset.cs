using Karpik.Engine.Shared.AssetManagement.Base;

namespace Karpik.Engine.Client.Graphics.Core.AssetManagement;

public abstract class Texture2DAsset : Asset
{
    public abstract ITexture2D Texture { get; }
}