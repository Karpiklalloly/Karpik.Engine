namespace DragonExtensions;

public class World(EcsWorld world)
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
}
