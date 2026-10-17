namespace Karpik.Engine.Core;

public interface IStaticRuntimeComposition
{
    void RegisterModules(IStaticModuleRegistry registry);
    void RegisterServices(IStaticServiceRegistry registry);
    void RegisterEcsRegistryProviders(IStaticEcsRegistryProviders registry);
}
