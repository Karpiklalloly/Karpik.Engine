using Karpik.Engine.Core;

namespace Karpik.Engine.Shared.Tweening;

[Module(ModuleScope.Simulation)]
public class TweenModuleInstaller : IModuleInstaller
{
    public string Name => "Tween.Core";

    public IModule? CreateModule() => new TweenModule();
}