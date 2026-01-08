using Karpik.Engine.Core;

namespace Karpik.Engine.Shared.ECS;

internal class DestroySystem : IEcsInit, IEcsDestroy
{
    [DI] private EcsDefaultWorld _defaultWorld;
    [DI] private EcsEventWorld _eventWorld;
    [DI] private EcsMetaWorld _metaWorld;
    
    public void Init()
    {
        BaseSystem.InitWorlds(_defaultWorld, _eventWorld, _eventWorld);
    }
    
    public void Destroy()
    {
        _defaultWorld.Destroy();
        _eventWorld.Destroy();
        _metaWorld.Destroy();
    }
}