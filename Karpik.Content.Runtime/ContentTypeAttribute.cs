namespace Karpik.Content.Runtime;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, AllowMultiple = false)]
public sealed class ContentTypeAttribute(string declaredType) : Attribute
{
    public string DeclaredType { get; } = declaredType;
}
