using DCFApixels.DragonECS;
using Karpik.Engine.Core;
using Microsoft.Extensions.Logging;
using KarpikGame.Shared;

[Module(ModuleScope.Simulation)]
public class ClientGameInstaller : IModuleInstaller
{
    public string Name => "KarpikGame.Client";

    public IModule CreateModule() => new ClientGameModule();
}

internal sealed class ClientGameModule : IModule
{
    public void Add(ISystemRegistry systems)
    {
        systems.Add<ClientGameInitSystem>();
        systems.Add<ClientGameRenderSystem>();
    }
}

public sealed class ClientGameInitSystem(EcsDefaultWorld world, ILogger<ClientGameInitSystem> logger) : ISystemInit
{
    public void Init()
    {
        logger.LogInformation("World has {count} entities. Client initialized.", world.Count);
    }
}

public sealed class ClientGameRenderSystem(ILogger<ClientGameRenderSystem> logger) : ISystemRender
{
    private int _renderedFrames;

    public void Render()
    {
        if (Interlocked.Increment(ref _renderedFrames) == 1)
        {
            logger.LogInformation("First frame rendered");
        }
    }
}
