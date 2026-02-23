using DCFApixels.DragonECS;
using Karpik.Engine.Client.Graphics.Core;
using Karpik.Engine.Core;

namespace Karpik.Engine.Client.InputModule;

[Module]
public class InputInstaller : IModule, IModuleConfiguratable
{
    public string Name => "Input";

    public void OnRegisterServices(IServiceRegister services)
    {
        services.Register(new Input());
    }

    public void OnConfigure(IServiceContainer services, out IEcsModule? module)
    {
        services.Get<Input>().Init(services.Get<IWindow>());
        module = new InputModuleEcs();
    }

    public void OnConfigureComplete(IServiceContainer services)
    {
        
    }
}