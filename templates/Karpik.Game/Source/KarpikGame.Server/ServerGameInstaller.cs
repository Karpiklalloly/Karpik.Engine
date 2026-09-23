using DCFApixels.DragonECS;
using Karpik.Engine.Core;
using Microsoft.Extensions.Logging;
using KarpikGame.Shared;

[Module(ModuleScope.Simulation)]
public class ServerGameInstaller : IModuleInstaller
{
    public string Name => "KarpikGame.Server";

    public IModule CreateModule() => new ServerGameModule();
}

internal sealed class ServerGameModule : IModule
{
    public void Add(ISystemRegistry systems)
    {
        systems.Add<ServerGameInitSystem>();
    }
}

public sealed class ServerGameInitSystem(EcsDefaultWorld world, ILogger<ServerGameInitSystem> logger) : ISystemInit
{
    public void Init()
    {
        if (world.Count == 0)
        {
            int entity = world.NewEntity();
            world.GetPool<GameComponent>().Add(entity) = new GameComponent { Value = 42 };
            logger.LogInformation("Created entity {entity} with GameComponent(42). Total entities: {count}", entity, world.Count);
        }

    }
}
