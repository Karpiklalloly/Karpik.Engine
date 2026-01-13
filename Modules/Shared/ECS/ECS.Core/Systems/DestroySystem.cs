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
        // Миры больше не уничтожаются здесь.
        // Их жизненный цикл теперь управляется Bootstrap и ECSInstaller
        // для поддержки горячей перезагрузки.
    }
}