namespace Karpik.Engine.Core;

internal interface IEngineRunner
{
    public void Setup(Application application, MainThreadScheduler scheduler,
        Dictionary<string, byte[]>? hotReloadData = null);

    public void RegisterTypes(Type[] types);

    public void Run(double dt);

    public void Destroy();

    public Dictionary<string, byte[]> GetHotReloadData();
}