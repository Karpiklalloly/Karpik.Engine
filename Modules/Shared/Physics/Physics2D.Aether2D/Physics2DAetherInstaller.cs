using Karpik.Engine.Core;
using Karpik.Engine.Shared.Physics.Core;

namespace Karpik.Engine.Shared.Physics.Aether2D;

[Module]
public class Physics2DAetherInstaller : IModule
{
    public string Name => "Physics2D.Aether2D";
    public void OnRegisterServices(IServiceRegister services)
    {
        services.Register<IPhysicsWorld2D>(new AetherPhysicsWorld());
    }
}