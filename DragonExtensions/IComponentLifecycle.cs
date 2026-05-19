using Karpik.Jobs;

namespace DragonExtensions;

public interface IComponentLifecycle<T> where T : struct, IEcsComponent
{
    public JobHandle<T> EnableAsync(T component, ComponentLifecycleContext context);
    public JobHandle DisableAsync(T component, ComponentLifecycleContext context);
}