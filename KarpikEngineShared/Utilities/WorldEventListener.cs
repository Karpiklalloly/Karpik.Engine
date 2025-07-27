namespace Karpik.Engine.Shared;

public class WorldEventListener : IEcsEntityEventListener
{
    public EcsWorld World => _world;
    
    private List<Action<int>> _onNew = [];
    private List<Action<int>> _onDel = [];
    private EcsWorld _world;

    public WorldEventListener(EcsWorld world)
    {
        _world = world;
        world.AddListener(this);
    }
    
    public void RegisterNew(Action<int> onNewEntity)
    {
        _onNew.Add(onNewEntity);
    }
    
    public void RegisterDel(Action<int> onDelEntity)
    {
        _onDel.Add(onDelEntity);
    }
    
    public void OnNewEntity(int entityID)
    {
        foreach (var action in _onNew)
        {
            action(entityID);
        }
    }

    public void OnDelEntity(int entityID)
    {
        foreach (var action in _onDel)
        {
            action(entityID);
        }
    }
}