using System.Reflection;
using DCFApixels.DragonECS;

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
    byte[] OnPrepareHotReload(IServiceContainer services);
    bool OnHotReload(byte[] data, IServiceContainer services);
}

public interface IModuleDestroy
{
    public void Destroy();
}

public interface IModuleConfiguratable
{
    public void OnConfigure(IServiceContainer services, out IEcsModule? module);
    public void OnConfigureComplete(IServiceContainer services);
}