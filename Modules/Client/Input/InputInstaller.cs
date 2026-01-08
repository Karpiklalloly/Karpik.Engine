using DCFApixels.DragonECS;
using Karpik.Engine.Client.Graphics.Core;
using Karpik.Engine.Core;

namespace Karpik.Engine.Client;

[Module]
public class InputInstaller : IModule
{
    public string Name => "Input";

    public void OnRegisterServices(IServiceRegister services)
    {
        services.Register(new Input());
    }

    public void OnConfigure(IServiceContainer services, out IEcsModule? module)
    {
        services.Get<Input>().Init(services.Get<IWindow>());
        module = new InputModule();
    }

    public void OnConfigureComplete(IServiceContainer services)
    {
        
    }
}