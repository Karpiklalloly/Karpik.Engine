using Karpik.Engine.Core;
using Xunit;

public sealed class ModuleLoaderExplicitBundleTests
{
    [Fact]
    public void ExplicitLoader_UsesBundleManifestAndBundleOwnedShadowDirectory()
    {
        string root = Path.Combine(Path.GetTempPath(), "KarpikModuleLoader", Guid.NewGuid().ToString("N"));
        string modules = Path.Combine(root, "modules.version.1");
        Directory.CreateDirectory(modules);
        try
        {
            string assembly = typeof(ModuleLoaderExplicitBundleTests).Assembly.Location;
            string name = Path.GetFileName(assembly);
            File.Copy(assembly, Path.Combine(modules, name));
            File.WriteAllText(Path.Combine(modules, "modules.list"), name + "\n");
            File.WriteAllText(Path.Combine(modules, ".complete"), RuntimeBundleLayout.ModuleCompletionMarker);

            (string shadow, WeakReference lifetime) = LoadAndDispose(root, modules);
            Assert.StartsWith(Path.Combine(root, "reload", "shadow") + Path.DirectorySeparatorChar,
                shadow, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(AppContext.BaseDirectory, shadow, StringComparison.OrdinalIgnoreCase);
            for (int attempt = 0; attempt < 10 && lifetime.IsAlive; attempt++)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
            }
            Assert.False(lifetime.IsAlive, "Explicit bundle PluginLoadContext must be collectible after ModuleLoader.Dispose.");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static (string Shadow, WeakReference Lifetime) LoadAndDispose(string root, string expectedModules)
    {
        var loader = new ModuleLoader(root);
        loader.LoadClientModules();
        Assert.Equal(expectedModules, loader.ModuleDirectory);
        Assert.Single(loader.LoadedAssemblies);
        string shadow = loader.ShadowCopyDirectory;
        loader.Dispose();
        return (shadow, Assert.IsType<WeakReference>(loader.LoadContextLifetime));
    }
}
