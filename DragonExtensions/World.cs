namespace DragonExtensions;

public class World(EcsWorld world)
{
    public EcsWorld Base => world;

    public entlong New()
    {
        lock (world)
        {
            return world.NewEntityLong();
        }
    }

    public entlong New(int id)
    {
        lock (world)
        {
            return world.NewEntityLong(id);
        }
    }

    public entlong New(ITemplateNode templateNode)
    {
        lock (world)
        {
            return world.NewEntityLong(templateNode);
        }
    }

    public entlong New(int id, ITemplateNode templateNode)
    {
        lock (world)
        {
            return world.NewEntityLong(id, templateNode);
        }
    }

    public ref T Event<T>() where T : struct, IEcsComponentEvent
    {
        lock (world)
        {
            return ref world.GetPool<T>().Add(New().ID);
        }
    }

    public void Del(int id)
    {
        lock (world)
        {
            world.DelEntity(id);
        }
    }

    public void Del(entlong entity)
    {
        lock (world)
        {
            world.DelEntity(entity);
        }
    }

    public bool TryDel(int id)
    {
        lock (world)
        {
            return world.TryDelEntity(id);
        }
    }

    public bool TryDel(entlong entity)
    {
        lock (world)
        {
            return world.TryDelEntity(entity);
        }
    }

    public EcsSpan Where<T>(out T aspect) where T : EcsAspect, new() => world.Where(out aspect);

    public EntityBuilder Start() => new EntityBuilder(world);

    public EntityBuilder Start(int id) => new EntityBuilder(world).WithId(id);

    public EcsPool<T> Pool<T>() where T : struct, IEcsComponent => world.GetPool<T>();

    public bool Has<T>(int entityId) where T : struct, IEcsComponent => world.GetPool<T>().Has(entityId);

    public ref T Get<T>(int entityId) where T : struct, IEcsComponent => ref world.GetPool<T>().Get(entityId);

    public bool Exists(int entityId) => world.IsUsed(entityId);

    public bool TryGet<T>(int entityId, out T component) where T : struct, IEcsComponent
    {
        lock (world)
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

    public entlong CommitBatch<T1>(T1 c1)
        where T1 : struct, IEcsComponent
    {
        lock (world)
        {
            int id = world.NewEntity();
            world.GetPool<T1>().Add(id) = c1;
            return world.GetEntityLong(id);
        }
    }

    public entlong CommitBatch<T1, T2>(T1 c1, T2 c2)
        where T1 : struct, IEcsComponent
        where T2 : struct, IEcsComponent
    {
        lock (world)
        {
            int id = world.NewEntity();
            world.GetPool<T1>().Add(id) = c1;
            world.GetPool<T2>().Add(id) = c2;
            return world.GetEntityLong(id);
        }
    }

    public entlong CommitBatch<T1, T2, T3>(T1 c1, T2 c2, T3 c3)
        where T1 : struct, IEcsComponent
        where T2 : struct, IEcsComponent
        where T3 : struct, IEcsComponent
    {
        lock (world)
        {
            int id = world.NewEntity();
            world.GetPool<T1>().Add(id) = c1;
            world.GetPool<T2>().Add(id) = c2;
            world.GetPool<T3>().Add(id) = c3;
            return world.GetEntityLong(id);
        }
    }

    public entlong CommitBatch<T1, T2, T3, T4>(T1 c1, T2 c2, T3 c3, T4 c4)
        where T1 : struct, IEcsComponent
        where T2 : struct, IEcsComponent
        where T3 : struct, IEcsComponent
        where T4 : struct, IEcsComponent
    {
        lock (world)
        {
            int id = world.NewEntity();
            world.GetPool<T1>().Add(id) = c1;
            world.GetPool<T2>().Add(id) = c2;
            world.GetPool<T3>().Add(id) = c3;
            world.GetPool<T4>().Add(id) = c4;
            return world.GetEntityLong(id);
        }
    }

    public entlong CommitBatch<T1, T2, T3, T4, T5>(T1 c1, T2 c2, T3 c3, T4 c4, T5 c5)
        where T1 : struct, IEcsComponent
        where T2 : struct, IEcsComponent
        where T3 : struct, IEcsComponent
        where T4 : struct, IEcsComponent
        where T5 : struct, IEcsComponent
    {
        lock (world)
        {
            int id = world.NewEntity();
            world.GetPool<T1>().Add(id) = c1;
            world.GetPool<T2>().Add(id) = c2;
            world.GetPool<T3>().Add(id) = c3;
            world.GetPool<T4>().Add(id) = c4;
            world.GetPool<T5>().Add(id) = c5;
            return world.GetEntityLong(id);
        }
    }

    public entlong CommitBatch<T1, T2, T3, T4, T5, T6>(T1 c1, T2 c2, T3 c3, T4 c4, T5 c5, T6 c6)
        where T1 : struct, IEcsComponent
        where T2 : struct, IEcsComponent
        where T3 : struct, IEcsComponent
        where T4 : struct, IEcsComponent
        where T5 : struct, IEcsComponent
        where T6 : struct, IEcsComponent
    {
        lock (world)
        {
            int id = world.NewEntity();
            world.GetPool<T1>().Add(id) = c1;
            world.GetPool<T2>().Add(id) = c2;
            world.GetPool<T3>().Add(id) = c3;
            world.GetPool<T4>().Add(id) = c4;
            world.GetPool<T5>().Add(id) = c5;
            world.GetPool<T6>().Add(id) = c6;
            return world.GetEntityLong(id);
        }
    }

    public entlong CommitBatch<T1, T2, T3, T4, T5, T6, T7>(T1 c1, T2 c2, T3 c3, T4 c4, T5 c5, T6 c6, T7 c7)
        where T1 : struct, IEcsComponent
        where T2 : struct, IEcsComponent
        where T3 : struct, IEcsComponent
        where T4 : struct, IEcsComponent
        where T5 : struct, IEcsComponent
        where T6 : struct, IEcsComponent
        where T7 : struct, IEcsComponent
    {
        lock (world)
        {
            int id = world.NewEntity();
            world.GetPool<T1>().Add(id) = c1;
            world.GetPool<T2>().Add(id) = c2;
            world.GetPool<T3>().Add(id) = c3;
            world.GetPool<T4>().Add(id) = c4;
            world.GetPool<T5>().Add(id) = c5;
            world.GetPool<T6>().Add(id) = c6;
            world.GetPool<T7>().Add(id) = c7;
            return world.GetEntityLong(id);
        }
    }

    public entlong CommitBatch<T1, T2, T3, T4, T5, T6, T7, T8>(T1 c1, T2 c2, T3 c3, T4 c4, T5 c5, T6 c6, T7 c7, T8 c8)
        where T1 : struct, IEcsComponent
        where T2 : struct, IEcsComponent
        where T3 : struct, IEcsComponent
        where T4 : struct, IEcsComponent
        where T5 : struct, IEcsComponent
        where T6 : struct, IEcsComponent
        where T7 : struct, IEcsComponent
        where T8 : struct, IEcsComponent
    {
        lock (world)
        {
            int id = world.NewEntity();
            world.GetPool<T1>().Add(id) = c1;
            world.GetPool<T2>().Add(id) = c2;
            world.GetPool<T3>().Add(id) = c3;
            world.GetPool<T4>().Add(id) = c4;
            world.GetPool<T5>().Add(id) = c5;
            world.GetPool<T6>().Add(id) = c6;
            world.GetPool<T7>().Add(id) = c7;
            world.GetPool<T8>().Add(id) = c8;
            return world.GetEntityLong(id);
        }
    }

    public void WithLock(Action action)
    {
        lock (world)
        {
            action();
        }
    }
}
