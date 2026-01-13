namespace Karpik.Engine.Core.ModuleManagement;

public interface IModuleHotReload
{
    void OnHotReload(IModule oldModule);
}
