namespace Karpik.Content.Runtime;

/// <summary>Supplies typed JSON metadata and a fallback for a manifest logical name.</summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class AutoBindJsonAttribute(string logicalName) : Attribute
{
    public string LogicalName { get; } = logicalName;
}
