namespace Karpik.Content.Runtime;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, AllowMultiple = false)]
public sealed class ContentAssetAttribute(string logicalName) : Attribute
{
    public string LogicalName { get; } = logicalName;
}
