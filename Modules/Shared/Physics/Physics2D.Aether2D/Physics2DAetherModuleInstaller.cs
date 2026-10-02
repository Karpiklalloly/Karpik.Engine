using System.Numerics;
using Autofac;
using Karpik.Engine.Core;
using nkast.Aether.Physics2D.Dynamics;

namespace Karpik.Engine.Shared.Physics.Aether2D;

[Module(ModuleScope.Simulation)]
public class Physics2DAetherModuleInstaller : IModuleInstaller
{
    public string Name => "Physics2D.Aether2D";
    public void OnRegisterServices(ContainerBuilder builder)
    {
        builder.Register(_ => new World(new Vector2(0, -9.8f).Aether))
            .AsSelf()
            .SingleInstance();
    }
}