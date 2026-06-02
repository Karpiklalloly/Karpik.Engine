using System.Numerics;
using Karpik.Engine.Core;
using Karpik.Engine.Shared.Physics.Core;
using nkast.Aether.Physics2D.Dynamics;

namespace Karpik.Engine.Shared.Physics.Aether2D;

[Module]
public class Physics2DAetherInstaller : IInstaller
{
    public string Name => "Physics2D.Aether2D";
    public void OnRegisterServices(IServiceRegister services, IServiceContainer serviceContainer)
    {
        services.Register(new World(new Vector2(0, -9.8f).Aether));
        services.Register<IPhysicsWorld2D>(new AetherPhysicsWorld());
    }
}