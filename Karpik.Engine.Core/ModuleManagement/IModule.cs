using System.Reflection;
using DCFApixels.DragonECS;

namespace Karpik.Engine.Core;

public interface IModule
{
    public string Name { get; }
    public void OnRegisterServices(IServiceRegister services);
    public void OnConfigure(IServiceContainer services, out IEcsModule? module);
    public void OnConfigureComplete(IServiceContainer services);
}

public interface IModuleListener
{
    public void OnAnotherModuleLoaded(IServiceContainer services, IModule anotherModule, Assembly anotherModuleAssembly);
}