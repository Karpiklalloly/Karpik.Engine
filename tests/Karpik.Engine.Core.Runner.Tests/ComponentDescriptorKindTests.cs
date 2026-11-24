using Karpik.Engine.Shared.Modding.Bindings;
using Xunit;

public sealed class ComponentDescriptorKindTests
{
    [Fact]
    public void Descriptor_RejectsAnotherKindEvenWhenAccessorImplementsBothInterfaces()
    {
        var descriptor = new ComponentFieldDescriptor("Value", true, (IComponentField<float>)new DualAccessor());
        Assert.Same(descriptor.F32, descriptor.Get<float>());
        Assert.Throws<InvalidOperationException>(() => descriptor.Get<int>());
        Assert.Throws<InvalidOperationException>(() => descriptor.I32);
    }
    private sealed class DualAccessor : IComponentField<float>, IComponentField<int>
    {
        float IComponentField<float>.Read(int entityId) => 1;
        int IComponentField<int>.Read(int entityId) => 2;
        public void Write(int entityId, float value) { }
        public void Write(int entityId, int value) { }
    }
}
