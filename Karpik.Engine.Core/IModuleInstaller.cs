using System.ComponentModel.Design;
using System.Reflection;
using Autofac;

namespace Karpik.Engine.Core;

public interface IModuleInstaller
{
    public string Name { get; }
    public void OnRegisterServices(ContainerBuilder builder);
    public IModule? CreateModule() => null;
}

public interface IModuleInstallerListener : IModuleInstaller
{
    public void OnAnotherModuleLoaded(IServiceContainer services, IModuleInstaller anotherModuleInstaller, Assembly anotherModuleAssembly);
}

/// <summary>
/// Legacy reload hook. Restart-worker hot reload v1 persists only ECS world state;
/// non-ECS modules should recreate runtime resources through normal lifecycle hooks.
/// </summary>
public interface IModuleInstallerHotReload : IModuleInstaller
{
    byte[] OnPrepareHotReload(IServiceContainer services);
    bool OnHotReload(byte[] data, IServiceResolver services);
}

public interface IModuleInstallerDestroy : IModuleInstaller
{
    public void Destroy();
}

public interface IModuleInstallerConfiguratable : IModuleInstaller
{
    public void OnConfigure(IServiceResolver services);
    public void OnConfigureComplete(IServiceResolver services);
}
