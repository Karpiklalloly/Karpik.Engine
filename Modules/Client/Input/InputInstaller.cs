using DCFApixels.DragonECS;
using Karpik.Engine.Core;
using Karpik.Engine.Modules.Window.Core;

namespace Karpik.Engine.Client.InputModule;

[Module]
public class InputInstaller : IInstaller, IInstallerConfiguratable
{
    public string Name => "Input";

    public void OnRegisterServices(IServiceRegister services)
    {
        services.Register(new Input());
    }

    public void OnConfigure(IServiceContainer services, IServiceRegister container, out IModule? module)
    {
        services.Get<Input>()!.Init(services.Get<IInputSource>()!, services.Get<InputCaptureState>()!);
        module = new InputModuleEcs();
    }

    public void OnConfigureComplete(IServiceContainer services)
    {
        
    }
}
