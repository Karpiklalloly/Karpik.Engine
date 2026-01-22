namespace Karpik.Engine.Shared.ECS;

public class WorldEventListener : IEcsEntityEventListener, IDisposable
{
    public event Action<int>? OnNewEntityCreated; 
    public event Action<int>? OnNewEntityDeleted; 
    
    public EcsWorld World => _world;
    
    private EcsWorld _world;

    public WorldEventListener(EcsWorld world)
    {
        _world = world;
        world.AddListener(this);
    }
    public void OnNewEntity(int entityID)
    {
        OnNewEntityCreated?.Invoke(entityID);
    }

    public void OnDelEntity(int entityID)
    {
        OnNewEntityDeleted?.Invoke(entityID);
    }

    public void Dispose()
    {
        OnNewEntityCreated = null;
        OnNewEntityDeleted = null;
        _world.RemoveListener(this);
    }
}