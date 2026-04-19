using Karpik.Engine.Shared.AssetManagement.Core;

namespace Karpik.Engine.Client.Graphics.Core.AssetManagement;

public class ShaderAsset : Asset
{
    public byte[] ShaderBytes { get; set; }
    public override Type ValueType => typeof(byte[]);

    public override object RawValue
    {
        get => ShaderBytes;
        set => ShaderBytes = (byte[])value;
    }
}