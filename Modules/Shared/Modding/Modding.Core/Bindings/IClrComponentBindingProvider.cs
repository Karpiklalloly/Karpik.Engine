using DCFApixels.DragonECS;

namespace Karpik.Engine.Shared.Modding.Bindings;

public interface IClrComponentBindingProvider
{
    ReadOnlyMemory<ClrComponentBindingDescriptor> GetBindings();
}

public abstract class ClrComponentBindingDescriptor
{
    public abstract Type ComponentType { get; }
    public abstract IComponentPoolAccess Bind(EcsDefaultWorld world);
}
