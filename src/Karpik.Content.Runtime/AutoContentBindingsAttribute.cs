namespace Karpik.Content.Runtime;

/// <summary>Declares the partial class that owns generated bindings for this project.</summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class AutoContentBindingsAttribute : Attribute;

/// <summary>Supplies typed JSON metadata and a fallback for a manifest logical name.</summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class AutoBindJsonAttribute(string logicalName) : Attribute
{
    public string LogicalName { get; } = logicalName;
}
