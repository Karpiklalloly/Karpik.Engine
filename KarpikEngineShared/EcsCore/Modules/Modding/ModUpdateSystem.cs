namespace Karpik.Engine.Shared.Modding;

// System can't be parallel because mods can have state that is not thread-safe
public class ModUpdateSystem : IEcsRun, IEcsInit
{
    [DI] private ModManager _modManager;

    public void Run()
    {
        _modManager.UpdateMods();
    }

    public void Init()
    {
        _modManager.StartMods();
    }
}