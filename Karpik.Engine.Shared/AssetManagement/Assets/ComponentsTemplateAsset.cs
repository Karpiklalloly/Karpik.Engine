namespace Karpik.Engine.Shared;

public class ComponentsTemplateAsset : Asset
{
    public ComponentsTemplate Template { get; set; }

    public override Type ValueType => typeof(ComponentsTemplate);

    public override object RawValue
    {
        get => Template;
        set => Template = (ComponentsTemplate)value;
    }
}