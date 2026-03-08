using System.ComponentModel.Design;
using System.Reflection;

namespace Karpik.Engine.Core;

public interface IModule
{
    public string Name { get; }
    public void OnRegisterServices(IServiceRegister services);
}

public interface IModuleListener : IModule
{
    public void OnAnotherModuleLoaded(IServiceContainer services, IModule anotherModule, Assembly anotherModuleAssembly);
}

public interface IModuleHotReload : IModule
{
    byte[] OnPrepareHotReload(IServiceContainer services);
    bool OnHotReload(byte[] data, IServiceContainer services);
}

public interface IModuleDestroy : IModule
{
    public void Destroy();
}

public interface IModuleConfiguratable : IModule
{
    public void OnConfigure(IServiceContainer services, IServiceRegister container);
    public void OnConfigureComplete(IServiceContainer services);
}