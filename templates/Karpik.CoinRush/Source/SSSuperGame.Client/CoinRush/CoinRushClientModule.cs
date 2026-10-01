using Autofac;
using DCFApixels.DragonECS;
using Karpik.Engine.Core;
using Karpik.Engine.Core.FileSystem;
using Karpik.Engine.Shared.DragonECS;
using Karpik.Engine.Shared.Network.Core;
using SSSuperGame.Shared.CoinRush;

namespace SSSuperGame.Client.CoinRush;

/// <summary>Registers the CoinRush client module and its network settings.</summary>
[Module(ModuleScope.Simulation)]
public sealed class CoinRushClientInstaller : IModuleInstaller
{
    /// <summary>Gets the module's registry name.</summary>
    public string Name => "CoinRush.Client";

    /// <summary>Creates the client gameplay module.</summary>
    /// <returns>A new CoinRush client module.</returns>
    public IModule CreateModule() => new CoinRushClientModule();

    /// <summary>Registers network settings from the source match configuration before network systems initialize.</summary>
    /// <param name="builder">Simulation service registrations.</param>
    public void OnRegisterServices(ContainerBuilder builder)
    {
        builder.Register(context =>
        {
            MatchConfig match = CoinRushContent.LoadMatch(context.Resolve<IFileSystem>());
            return new NetworkConfig { Port = match.ServerPort, Key = match.ServerKey };
        }).AsSelf().SingleInstance();
    }
}

/// <summary>Defines the client systems and match-event dispatch pipeline.</summary>
public sealed class CoinRushClientModule : IModule
{
    /// <summary>Adds bootstrap, networking, presentation, and diagnostic systems.</summary>
    /// <param name="systems">Registry receiving the client systems.</param>
    public void Add(ISystemRegistry systems)
    {
        systems.Add<ClientBootstrapSystem>();
        systems.Add<ClientInputSystem>();
        systems.Add<ClientSnapshotSystem>();
        systems.Add<ClientEventSystem>();
        systems.ConfigurePipeline(static builder =>
        {
            builder.AddCaller<MatchEvent>();
            builder.AddCaller<FxEvent>();
        });
        systems.Add<ClientFxSystem>();
        systems.Add<ClientCameraSystem>();
        systems.Add<ClientDrawSystem>();
        systems.Add<ClientDebugSystem>();
    }
}
