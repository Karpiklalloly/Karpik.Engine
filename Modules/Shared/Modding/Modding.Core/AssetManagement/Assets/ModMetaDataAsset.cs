using Karpik.Engine.Shared.AssetManagement.Base;

namespace Karpik.Engine.Shared.Modding;

public class ModMetaDataAsset : Asset
{
    public ModMetaData MetaData { get; set; }
    public override Type ValueType => typeof(ModMetaData);

    public override object RawValue
    {
        get => MetaData;
        set => MetaData = (ModMetaData)value;
    }
}