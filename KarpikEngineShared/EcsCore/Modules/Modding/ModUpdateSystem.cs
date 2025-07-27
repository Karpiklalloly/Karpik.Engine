namespace Karpik.Engine.Shared.Modding;

public class ModUpdateSystem : IEcsRun, IEcsInit, IEcsInject<ModManager>
{
    private ModManager _modManager;

    public void Run()
    {
        _modManager.UpdateMods();
    }

    public void Init()
    {
        _modManager.StartMods();
    }

    public void Inject(ModManager obj)
    {
        _modManager = obj;
    }
}