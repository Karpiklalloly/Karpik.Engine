using DCFApixels.DragonECS;

namespace Karpik.Engine.Core;

public interface IModuleConfiguratable
{
    public void OnConfigure(IServiceContainer services, out IEcsModule? module);
    public void OnConfigureComplete(IServiceContainer services);
}