namespace Karpik.Jobs;

public readonly struct ValueJobHandle
{
    internal ValueJobHandle(JobDescriptorHandle descriptor)
    {
        Descriptor = descriptor;
    }

    internal JobDescriptorHandle Descriptor { get; }
    public bool IsValid => Descriptor.IsValid;
}
