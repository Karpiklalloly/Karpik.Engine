using System.Reflection;

namespace Karpik.Engine.Core;

public interface IInstaller
{
    public string Name { get; }
    public void OnRegisterServices(IServiceRegister services, IServiceContainer serviceContainer);
}

public interface IInstallerListener : IInstaller
{
    public void OnAnotherModuleLoaded(IServiceContainer services, IInstaller anotherInstaller, Assembly anotherModuleAssembly);
}

/// <summary>
/// Legacy reload hook. Restart-worker hot reload v1 persists only ECS world state;
/// non-ECS modules should recreate runtime resources through normal lifecycle hooks.
/// </summary>
public interface IInstallerHotReload : IInstaller
{
    byte[] OnPrepareHotReload(IServiceContainer services);
    bool OnHotReload(byte[] data, IServiceContainer services);
}

public interface IInstallerDestroy : IInstaller
{
    public void Destroy();
}

public interface IInstallerConfiguratable : IInstaller
{
    public void OnConfigure(IServiceContainer services, IServiceRegister container, out IModule? module);
    public void OnConfigureComplete(IServiceContainer services);
}
