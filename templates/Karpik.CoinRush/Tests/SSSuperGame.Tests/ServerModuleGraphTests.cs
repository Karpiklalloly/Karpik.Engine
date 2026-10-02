using System.Reflection;
using System.Text.Json;
using Karpik.Engine.Core;
using Xunit;

namespace SSSuperGame.Tests;

/// <summary>Checks generated server module composition and engine version pinning.</summary>
public sealed class ServerModuleGraphTests
{
    /// <summary>Finds the repository root from the solution file.</summary>
    /// <returns>The absolute repository root path.</returns>
    private static string RepoRoot()
    {
        string? dir = AppContext.BaseDirectory;
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir, "SSSuperGame.slnx")))
            {
                return dir;
            }
            dir = Directory.GetParent(dir)?.FullName;
        }
        throw new InvalidOperationException("Repo root not found.");
    }

    /// <summary>Records installers registered by generated module composition.</summary>
    private sealed class CapturingRegistry : IStaticModuleRegistry
    {
        public readonly List<IModuleInstaller> Installers = new();
        /// <summary>Records a module installer in the capturing registry.</summary>
        /// <param name="installer">The installer to record.</param>
        public void Add(IModuleInstaller installer) => Installers.Add(installer);
    }

    /// <summary>Verifies the server composition includes CoinRush and engine modules.</summary>
    [Fact]
    public void Server_graph_contains_coinrush_and_engine_modules()
    {
        string serverDll = Path.Combine(RepoRoot(), "Source", "SSSuperGame.Server", "bin", "Debug", "net10.0", "SSSuperGame.Server.dll");
        Assert.True(File.Exists(serverDll), $"Build the Server project first: {serverDll}.");
        string launcherDir = Path.Combine(RepoRoot(), "Source", "SSSuperGame.Server.Launcher", "bin", "Debug", "net10.0");
        ResolveEventHandler? resolver = null;
        resolver = (sender, args) =>
        {
            string name = new AssemblyName(args.Name).Name + ".dll";
            string candidate = Path.Combine(launcherDir, name);
            return File.Exists(candidate) ? Assembly.LoadFrom(candidate) : null;
        };
        AppDomain.CurrentDomain.AssemblyResolve += resolver;
        try
        {
            AssertGraph(serverDll);
        }
        finally
        {
            AppDomain.CurrentDomain.AssemblyResolve -= resolver;
        }
    }

    /// <summary>Checks the installers registered by the server assembly.</summary>
    /// <param name="serverDll">The server assembly path.</param>
    private static void AssertGraph(string serverDll)
    {
        Assembly server = Assembly.LoadFrom(serverDll);
        Type? comp = server.GetTypes().FirstOrDefault(t => typeof(IStaticRuntimeComposition).IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract);
        Assert.NotNull(comp);
        var instance = (IStaticRuntimeComposition)Activator.CreateInstance(comp!)!;
        var registry = new CapturingRegistry();
        instance.RegisterModules(registry);
        string[] names = registry.Installers.Select(i => i.GetType().FullName ?? i.GetType().Name).ToArray();
        Assert.Contains(names, n => n.Contains("CoinRushServerInstaller") || n.Contains("CoinRush.Server"));
        Assert.Contains(names, n => n.Contains("EcsModuleInstaller"));
        Assert.Contains(names, n => n.Contains("Physics2D"));
        Assert.Contains(names, n => n.Contains("NetworkServer") || n.Contains("LiteNetLib"));
    }

    /// <summary>Verifies the test SDK version matches the pinned engine SDK.</summary>
    [Fact]
    public void Engine_pin_matches_global_json()
    {
        string root = RepoRoot();
        using JsonDocument global = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "global.json")));
        string pinned = global.RootElement.GetProperty("msbuild-sdks").GetProperty("Karpik.Engine.Sdk").GetString()!;
        string csproj = File.ReadAllText(Path.Combine(root, "Tests", "SSSuperGame.Tests", "SSSuperGame.Tests.csproj"));
        Assert.Contains($"<KarpikTestSdkVersion>{pinned}</KarpikTestSdkVersion>", csproj);
        string? localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string[] matches = Directory.GetFiles(
            Path.Combine(localAppData, "Karpik", "Engines"),
            $"Karpik.Engine.Sdk.{pinned}.nupkg",
            SearchOption.AllDirectories);
        Assert.True(matches.Length >= 1, $"Engine SDK {pinned} not installed.");
    }
}
