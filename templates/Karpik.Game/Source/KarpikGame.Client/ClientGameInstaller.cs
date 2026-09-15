using DCFApixels.DragonECS;
using Karpik.Engine.Core;
using Karpik.Engine.Core.FileSystem;
using Karpik.Engine.Shared.AssetManagement.Core;
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

public sealed class ClientGameInitSystem(EcsDefaultWorld world, ILogger<ClientGameInitSystem> logger, IFileSystem fileSystem) : ISystemInit
{
    public void Init()
    {
        logger.LogInformation("World has {count} entities. Client initialized.", world.Count);

        string contentPath = fileSystem.Combine(Directory.GetCurrentDirectory(), "Content", "runtime.txt");
        if (File.Exists(contentPath))
        {
            logger.LogInformation("Content: {content}", File.ReadAllText(contentPath).Trim());
        }
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
