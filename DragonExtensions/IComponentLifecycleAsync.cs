using Karpik.Jobs;

namespace DragonExtensions;

public interface IComponentLifecycleAsync<T> where T : struct, IEcsComponent
{
    public JobHandle<T> EnableAsync(T component, ComponentLifecycleContext context);
    public JobHandle<T> DisableAsync(T component, ComponentLifecycleContext context);
}