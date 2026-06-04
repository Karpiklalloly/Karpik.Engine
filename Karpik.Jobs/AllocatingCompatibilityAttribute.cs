namespace Karpik.Jobs;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method | AttributeTargets.Constructor)]
public sealed class AllocatingCompatibilityAttribute : Attribute
{
    public AllocatingCompatibilityAttribute(string reason)
    {
        Reason = reason;
    }

    public string Reason { get; }
    public bool AllocatesManagedMemory => true;
    public bool IsHotPathSafe => false;
}
