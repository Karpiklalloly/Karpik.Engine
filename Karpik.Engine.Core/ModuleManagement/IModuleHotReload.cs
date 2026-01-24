using Karpik.Engine.Core.Hot;

namespace Karpik.Engine.Core.ModuleManagement;

public interface IModuleHotReload
{
    void OnPrepareHotReload();
    bool OnHotReload(IModule oldModule, TypeMapper map, IServiceContainer services);
}
