using Karpik.Engine.Core;
using Karpik.Jobs;

namespace DragonExtensions;

public class World(EcsWorld world, IServiceContainer container)
{
    public EcsWorld Base => world;

    public entlong New()
    {
        return world.NewEntityLong();
    }

    public entlong New(int id)
    {
        return world.NewEntityLong(id);
    }

    public entlong New(ITemplateNode templateNode)
    {
        return world.NewEntityLong(templateNode);
    }

    public entlong New(int id, ITemplateNode templateNode)
    {
        return world.NewEntityLong(id, templateNode);
    }

    public ref T Event<T>() where T : struct, IEcsComponentEvent
    {
        return ref world.GetPool<T>().Add(New().ID);
    }

    public void Del(int id)
    {
        world.DelEntity(id);
    }

    public void Del(entlong entity)
    {
        world.DelEntity(entity);
    }

    public bool TryDel(int id)
    {
        return world.TryDelEntity(id);
    }

    public bool TryDel(entlong entity)
    {
        return world.TryDelEntity(entity);
    }

    public void Add<T>(int entityId, in T component) where T : struct, IEcsComponent
    {
        world.GetPool<T>().Add(entityId) = component;
    }
    
    public void Set<T>(int entityId, in T component) where T : struct, IEcsComponent
    {
        world.GetPool<T>().Get(entityId) = component;
    }

    public void Del<T>(int entityId) where T : struct, IEcsComponent
    {
        world.GetPool<T>().Del(entityId);
    }

    public EcsSpan Where<T>(out T aspect) where T : EcsAspect, new() => world.Where(out aspect);

    public bool Has<T>(int entityId) where T : struct, IEcsComponent => world.GetPool<T>().Has(entityId);

    public ref T Get<T>(int entityId) where T : struct, IEcsComponent => ref world.GetPool<T>().Get(entityId);

    public bool Exists(int entityId) => world.IsUsed(entityId);

    public bool TryGet<T>(int entityId, out T component) where T : struct, IEcsComponent
    {
        var pool = world.GetPool<T>();
        if (pool.Has(entityId))
        {
            component = pool.Get(entityId);
            return true;
        }
        component = default;
        return false;
    }

    public async JobHandle EnableAsync<T>(int entityId) where T : struct, IEcsComponent, IComponentLifecycleAsync<T>
    {
        try
        {
            var pool = world.GetPool<T>();
            var component = pool.Get(entityId);
            component = await component.EnableAsync(new ComponentLifecycleContext(container, world, entityId));
            pool.Get(entityId) = component;
        }
        catch (Exception ex) when (ex is not ComponentLifecycleException)
        {
            throw CreateLifecycleException<T>("EnableAsync", entityId, ex);
        }
    }
    
    public void Enable<T>(int entityId) where T : struct, IEcsComponent, IComponentLifecycle<T>
    {
        try
        {
            var pool = world.GetPool<T>();
            ref var component = ref pool.Get(entityId);
            component.Enable(new ComponentLifecycleContext(container, world, entityId));
        }
        catch (Exception ex) when (ex is not ComponentLifecycleException)
        {
            throw CreateLifecycleException<T>("Enable", entityId, ex);
        }
    }

    public async JobHandle AddEnabledAsync<T>(int entityId, T component) where T : struct, IEcsComponent, IComponentLifecycleAsync<T>
    {
        try
        {
            var pool = world.GetPool<T>();
            pool.Add(entityId) = component;
            component = pool.Get(entityId);
            component = await component.EnableAsync(new ComponentLifecycleContext(container, world, entityId));
            pool.Get(entityId) = component;
        }
        catch (Exception ex) when (ex is not ComponentLifecycleException)
        {
            throw CreateLifecycleException<T>("AddEnabledAsync", entityId, ex);
        }
    }
    
    public void AddEnabled<T>(int entityId, T component) where T : struct, IEcsComponent, IComponentLifecycle<T>
    {
        try
        {
            var pool = world.GetPool<T>();
            pool.Add(entityId) = component;
            ref var stored = ref pool.Get(entityId);
            stored.Enable(new ComponentLifecycleContext(container, world, entityId));
        }
        catch (Exception ex) when (ex is not ComponentLifecycleException)
        {
            throw CreateLifecycleException<T>("AddEnabled", entityId, ex);
        }
    }
    
    public async JobHandle DisableAsync<T>(int entityId) where T : struct, IEcsComponent, IComponentLifecycleAsync<T>
    {
        try
        {
            var pool = world.GetPool<T>();
            var component = pool.Get(entityId);
            component = await component.DisableAsync(new ComponentLifecycleContext(container, world, entityId));
            pool.Get(entityId) = component;
        }
        catch (Exception ex) when (ex is not ComponentLifecycleException)
        {
            throw CreateLifecycleException<T>("DisableAsync", entityId, ex);
        }
    }
    
    public void Disable<T>(int entityId) where T : struct, IEcsComponent, IComponentLifecycle<T>
    {
        try
        {
            var pool = world.GetPool<T>();
            ref var component = ref pool.Get(entityId);
            component.Disable(new ComponentLifecycleContext(container, world, entityId));
        }
        catch (Exception ex) when (ex is not ComponentLifecycleException)
        {
            throw CreateLifecycleException<T>("Disable", entityId, ex);
        }
    }
    
    public async JobHandle DelEnabledAsync<T>(int entityId) where T : struct, IEcsComponent, IComponentLifecycleAsync<T>
    {
        try
        {
            var pool = world.GetPool<T>();
            var component = pool.Get(entityId);
            component = await component.DisableAsync(new ComponentLifecycleContext(container, world, entityId));
            pool.Del(entityId);
        }
        catch (Exception ex) when (ex is not ComponentLifecycleException)
        {
            throw CreateLifecycleException<T>("DelEnabledAsync", entityId, ex);
        }
    }
    
    public void DelEnabled<T>(int entityId) where T : struct, IEcsComponent, IComponentLifecycle<T>
    {
        try
        {
            var pool = world.GetPool<T>();
            ref var component = ref pool.Get(entityId);
            component.Disable(new ComponentLifecycleContext(container, world, entityId));
            pool.Del(entityId);
        }
        catch (Exception ex) when (ex is not ComponentLifecycleException)
        {
            throw CreateLifecycleException<T>("DelEnabled", entityId, ex);
        }
    }

    private ComponentLifecycleException CreateLifecycleException<T>(string phase, int entityId, Exception exception)
        where T : struct, IEcsComponent
    {
        return new ComponentLifecycleException(phase, world, entityId, typeof(T), exception);
    }
}
