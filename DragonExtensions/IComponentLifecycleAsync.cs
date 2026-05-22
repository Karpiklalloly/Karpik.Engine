using Karpik.Jobs;

namespace DragonExtensions;

public interface IComponentLifecycleAsync<T> where T : struct, IEcsComponent
{
    public JobHandle<T> EnableAsync(ComponentLifecycleContext context);
    public JobHandle<T> DisableAsync(ComponentLifecycleContext context);
}

public interface IComponentLifecycle<T> where T : struct, IEcsComponent
{
    public void Enable(ComponentLifecycleContext context);
    public void Disable(ComponentLifecycleContext context);
}