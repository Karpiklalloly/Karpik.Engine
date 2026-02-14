using System.Reflection;

namespace Karpik.Engine.Core;

public interface IModule
{
    public string Name { get; }
    public void OnRegisterServices(IServiceRegister services);
}

public interface IModuleListener
{
    public void OnAnotherModuleLoaded(IServiceContainer services, IModule anotherModule, Assembly anotherModuleAssembly);
}

public interface IModuleHotReload
{
    void OnPrepareHotReload();
    bool OnHotReload(IModule oldModule, IServiceContainer services);
}

public interface IModuleDestroy
{
    public void Destroy();
}