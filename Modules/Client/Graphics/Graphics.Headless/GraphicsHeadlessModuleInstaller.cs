using Autofac;
using Karpik.Engine.Client.Graphics.Core;
using Karpik.Engine.Core;

namespace Karpik.Engine.Client.Graphics.Headless;

[Module(ModuleScope.Engine, -102)]
public sealed class GraphicsHeadlessModuleInstaller : IModuleInstaller
{
    public string Name => "Graphics.Headless";

    public void OnRegisterServices(ContainerBuilder builder)
    {
    }
}
