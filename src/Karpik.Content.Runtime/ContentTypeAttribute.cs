namespace Karpik.Content.Runtime;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Interface, AllowMultiple = false)]
public sealed class ContentTypeAttribute(string declaredType) : Attribute
{
    public string DeclaredType { get; } = declaredType;
}
