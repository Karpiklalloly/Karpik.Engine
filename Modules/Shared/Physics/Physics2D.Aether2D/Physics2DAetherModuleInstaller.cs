using System.Numerics;
using Autofac;
using Karpik.Engine.Core;
using Karpik.Engine.Shared.Physics.Core;
using Microsoft.Extensions.DependencyInjection;
using nkast.Aether.Physics2D.Dynamics;

namespace Karpik.Engine.Shared.Physics.Aether2D;

[Module]
public class Physics2DAetherModuleInstaller : IModuleInstaller
{
    public string Name => "Physics2D.Aether2D";
    public void OnRegisterServices(ContainerBuilder builder)
    {
        builder.Register(new World(new Vector2(0, -9.8f).Aether));
        builder.Register<IPhysicsWorld2D>(new AetherPhysicsWorld());
    }
}