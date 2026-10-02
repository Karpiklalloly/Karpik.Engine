using Karpik.Engine.Core;

namespace Karpik.Engine.Modules.Window.Headless;

[Module(ModuleScope.Engine, -200)]
public sealed class WindowHeadlessModuleInstaller : IModuleInstaller
{
    public string Name => "Window.Headless";
}
