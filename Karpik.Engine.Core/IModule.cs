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
    byte[] OnPrepareHotReload();
    bool OnHotReload(byte[] data, IServiceContainer services);
}

public interface IModuleDestroy
{
    public void Destroy();
}