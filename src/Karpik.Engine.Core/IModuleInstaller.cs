using Autofac;

namespace Karpik.Engine.Core;

public interface IModuleInstaller
{
    public string Name { get; }
    public void OnRegisterServices(ContainerBuilder builder) { }
    public IModule? CreateModule() => null;
}
