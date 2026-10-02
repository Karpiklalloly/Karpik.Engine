using Autofac;
using DCFApixels.DragonECS;
using Karpik.Engine.Core;
using Karpik.Engine.Core.FileSystem;
using Karpik.Engine.Shared.DragonECS;
using Karpik.Engine.Shared.Network.Core;
using SSSuperGame.Shared.CoinRush;

namespace SSSuperGame.Server.CoinRush;

/// <summary>Registers the CoinRush server module and its network settings.</summary>
[Module(ModuleScope.Simulation)]
public sealed class CoinRushServerInstaller : IModuleInstaller
{
    /// <summary>Gets the server module name.</summary>
    public string Name => "CoinRush.Server";

    /// <summary>Creates the server simulation module.</summary>
    /// <returns>The module that adds the server systems.</returns>
    public IModule CreateModule() => new CoinRushServerModule();

    /// <summary>Registers the network configuration from the match content before systems initialize.</summary>
    /// <param name="builder">The simulation service container builder.</param>
    public void OnRegisterServices(ContainerBuilder builder)
    {
        builder.Register(context =>
        {
            MatchConfig match = CoinRushContent.LoadMatch(context.Resolve<IFileSystem>());
            return new NetworkConfig { Port = match.ServerPort, Key = match.ServerKey };
        }).AsSelf().SingleInstance();
    }
}

/// <summary>Adds the server bootstrap, transport, gameplay, and lifecycle systems.</summary>
public sealed class CoinRushServerModule : IModule
{
    /// <summary>Registers the server systems and match event caller.</summary>
    /// <param name="systems">The engine system registry.</param>
    public void Add(ISystemRegistry systems)
    {
        systems.Add<ServerBootstrapSystem>();
        systems.Add<ServerNetSystem>();
        systems.Add<ServerMatchEventSystem>();
        systems.ConfigurePipeline(static builder => builder.AddCaller<MatchEvent>());
        systems.Add<PlayerPhysicsSystem>();
        systems.Add<MatchSystem>();
    }
}
