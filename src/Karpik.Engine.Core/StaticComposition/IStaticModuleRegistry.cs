namespace Karpik.Engine.Core;

public interface IStaticModuleRegistry
{
    void Add(IModuleInstaller installer);
}
