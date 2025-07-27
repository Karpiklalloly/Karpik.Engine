namespace Network;

[AttributeUsage(AttributeTargets.Struct | AttributeTargets.Class)]
public class NetworkedComponentAttribute : Attribute { }

[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public class NetworkedFieldAttribute : Attribute
{
    public float Precision { get; set; } = 0.01f;
}