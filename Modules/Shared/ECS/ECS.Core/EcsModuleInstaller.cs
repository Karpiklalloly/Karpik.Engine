using Autofac;
using Karpik.Engine.Core;

namespace Karpik.Engine.Shared.ECS;

[Module(ModuleScope.Simulation)]
public class EcsModuleInstaller : IModuleInstaller
{
    public string Name => "ECS.Core";

    public void OnRegisterServices(ContainerBuilder builder)
    {
        builder.RegisterType<EcsDefaultWorld>()
            .AsSelf()
            .SingleInstance()
            .OnRelease(static world => world.Destroy());
        
        builder.RegisterType<EcsEventWorld>()
            .AsSelf()
            .SingleInstance()
            .OnRelease(static world => world.Destroy());
        
        builder.RegisterType<EcsMetaWorld>()
            .AsSelf()
            .SingleInstance()
            .OnRelease(static world => world.Destroy());
    }
}
