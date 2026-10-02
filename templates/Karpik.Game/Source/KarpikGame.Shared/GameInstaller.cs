using Autofac;
using Karpik.Engine.Core;

[Module(ModuleScope.Simulation)]
public class GameInstaller : IModuleInstaller
{
    public string Name => "KarpikGame.Shared";

    public void OnRegisterServices(ContainerBuilder builder)
    {
        Console.WriteLine($"[Game] {nameof(GameInstaller)} registered");
    }
}
