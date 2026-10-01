using System.Reflection;
using Autofac;
using Karpik.Engine.Core;
using Karpik.Engine.Core.FileSystem;
using Karpik.Engine.Shared.Network.Core;
using Xunit;

namespace SSSuperGame.Tests;

/// <summary>Checks generated client module composition and installer settings.</summary>
public sealed class ModuleGraphTests
{
    /// <summary>Verifies the client installer reads network settings before initialization.</summary>
    [Fact]
    public void Client_installer_registers_match_network_settings_before_init()
    {
        DirectoryInfo root = Directory.CreateTempSubdirectory("CoinRush-test-");
        try
        {
            string content = Path.Combine(root.FullName, "Content");
            string matchDir = Path.Combine(content, "CoinRush");
            Directory.CreateDirectory(matchDir);
            File.WriteAllText(Path.Combine(matchDir, "Match.json"),
                """{"MatchDuration":30,"CountdownDuration":3,"CoinRespawnDelay":2,"MaxPlayers":2,"ServerPort":19555,"ServerKey":"test-key"}""");
            var builder = new ContainerBuilder();
            builder.RegisterInstance(new RemappedFileSystem(root.FullName, content)).As<IFileSystem>();
            ((IModuleInstaller)new SSSuperGame.Client.CoinRush.CoinRushClientInstaller()).OnRegisterServices(builder);
            using var container = builder.Build();
            NetworkConfig config = container.Resolve<NetworkConfig>();
            Assert.Equal(19555, config.Port);
            Assert.Equal("test-key", config.Key);
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    /// <summary>Records installers registered by generated module composition.</summary>
    private sealed class CapturingRegistry : IStaticModuleRegistry
    {
        public readonly List<IModuleInstaller> Installers = new();
        /// <summary>Records a module installer in the capturing registry.</summary>
        /// <param name="installer">The installer to record.</param>
        public void Add(IModuleInstaller installer) => Installers.Add(installer);
    }

    /// <summary>Runs generated module composition against a capturing registry.</summary>
    /// <param name="assembly">The assembly whose module composition is inspected.</param>
    /// <returns>A registry containing the composed installers.</returns>
    private static CapturingRegistry Compose(Assembly assembly)
    {
        Type? comp = assembly.GetTypes().FirstOrDefault(t => typeof(IStaticRuntimeComposition).IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract);
        Assert.NotNull(comp);
        var instance = (IStaticRuntimeComposition)Activator.CreateInstance(comp!)!;
        var registry = new CapturingRegistry();
        instance.RegisterModules(registry);
        return registry;
    }

    /// <summary>Verifies the client composition includes CoinRush and engine modules.</summary>
    [Fact]
    public void Client_graph_contains_coinrush_and_engine_modules()
    {
        Assembly client = typeof(SSSuperGame.Client.ClientGreeting).Assembly;
        CapturingRegistry registry = Compose(client);
        string[] names = registry.Installers.Select(i => i.GetType().FullName ?? i.GetType().Name).ToArray();
        Assert.Contains(names, n => n.Contains("CoinRushClientInstaller") || n.Contains("CoinRush.Client"));
        Assert.Contains(names, n => n.Contains("EcsModuleInstaller"));
        Assert.Contains(names, n => n.Contains("Physics2D"));
        Assert.Contains(names, n => n.Contains("NetworkClient") || n.Contains("LiteNetLib"));
        Assert.Contains(names, n => n.Contains("Graphics"));
        Assert.Contains(names, n => n.Contains("Input"));
        Assert.Contains(names, n => n.Contains("Tween"));
    }

    /// <summary>Verifies that all game installers use simulation scope.</summary>
    [Fact]
    public void All_game_installers_use_simulation_scope()
    {
        Assembly client = typeof(SSSuperGame.Client.ClientGreeting).Assembly;
        Type[] installers = client.GetTypes()
            .Where(t => typeof(IModuleInstaller).IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract)
            .ToArray();
        Assert.NotEmpty(installers);
        foreach (Type t in installers)
        {
            var attr = t.GetCustomAttribute<ModuleAttribute>();
            if (t.FullName?.Contains("CoinRush") == true)
            {
                Assert.NotNull(attr);
                Assert.Equal(ModuleScope.Simulation, attr!.Scope);
            }
        }
    }
}
