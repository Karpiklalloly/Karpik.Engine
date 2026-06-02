using DCFApixels.DragonECS;
using DCFApixels.DragonECS.RunnersCore;
using DragonExtensions;
using System.Reflection;

namespace Karpik.Engine.Core.Runner;

public class Builder(EcsPipeline.Builder builder) : IBuilder
{
    public IBuilder Add(object system, string layer = "BASIC_LAYER", int order = 0)
    {
        bool added = false;
        if (system is ISystemInit init)
        {
            Add(init, layer, order);
            added = true;
        }
        if (system is ISystemBegin begin)
        {
            Add(begin, layer, order);
            added = true;
        }
        if (system is ISystemFixedUpdate fixedUpdate)
        {
            Add(fixedUpdate, layer, order);
            added = true;
        }
        if (system is ISystemUpdate update)
        {
            Add(update, layer, order);
            added = true;
        }
        if (system is ISystemLateUpdate end)
        {
            Add(end, layer, order);
            added = true;
        }
        if (system is ISystemRender render)
        {
            Add(render, layer, order);
            added = true;
        }
        if (system is ISystemDestroy destroy)
        {
            Add(destroy, layer, order);
            added = true;
        }

        if (!added && system is IEcsProcess process)
        {
            builder.Add(process, layer, order);
            added = true;
        }

        if (!added)
        {
            Console.WriteLine($"[Builder] Warning: System of type {system.GetType().FullName} does not implement any known system interfaces and will not be added to the pipeline.");
        }
        
        return this;
    }

    public IBuilder Add(ISystemInit init, string layer = "BASIC_LAYER", int order = 0)
    {
        builder.Add(new InitSystem(init), layer, order);
        
        return this;
    }

    public IBuilder Add(ISystemBegin begin, string layer = "BASIC_LAYER", int order = 0)
    {
        builder.Add(new BeginSystem(begin), layer, order);
        
        return this;
    }

    public IBuilder Add(ISystemFixedUpdate fixedUpdate, string layer = "BASIC_LAYER", int order = 0)
    {
        builder.Add(new FixedUpdateSystem(fixedUpdate), layer, order);
        
        return this;
    }

    public IBuilder Add(ISystemUpdate update, string layer = "BASIC_LAYER", int order = 0)
    {
        builder.Add(new UpdateSystem(update), layer, order);
        
        return this;
    }

    public IBuilder Add(ISystemLateUpdate end, string layer = "BASIC_LAYER", int order = 0)
    {
        builder.Add(new LateSystem(end), layer, order);
        
        return this;
    }

    public IBuilder Add(ISystemRender render, string layer = "BASIC_LAYER", int order = 0)
    {
        builder.Add(new RenderSystem(render), layer, order);
        
        return this;
    }

    public IBuilder Add(ISystemDestroy destroy, string layer = "BASIC_LAYER", int order = 0)
    {
        builder.Add(new DragonExtensions.DestroySystem(destroy), layer, order);
        
        return this;
    }

    public IBuilder AddRunner<TRunner>() where TRunner : new()
    {
        Type runnerType = typeof(TRunner);
        if (!typeof(EcsRunner).IsAssignableFrom(runnerType) ||
            !typeof(IEcsRunner).IsAssignableFrom(runnerType))
        {
            throw new InvalidOperationException(
                $"Runner {runnerType.FullName} must inherit {nameof(EcsRunner)} and implement {nameof(IEcsRunner)}.");
        }

        MethodInfo method = typeof(EcsPipeline.Builder)
            .GetMethod(nameof(EcsPipeline.Builder.AddRunner), BindingFlags.Instance | BindingFlags.Public)!
            .MakeGenericMethod(runnerType);

        method.Invoke(builder, null);
        return this;
    }
}
