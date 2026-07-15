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
            string assembly = typeof(FactAttribute).Assembly.Location;
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
            ModuleLoader.CleanupDisposedShadows();
            Assert.False(Directory.Exists(shadow), "Explicit loader disposal must not leave a standalone shadow directory.");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData("crlf")]
    [InlineData("bom")]
    public void ExplicitLoader_RejectsNonCanonicalManifestBytes(string format)
    {
        string root = Path.Combine(Path.GetTempPath(), "KarpikModuleLoaderManifest", Guid.NewGuid().ToString("N"));
        string modules = Path.Combine(root, "modules.version.1");
        Directory.CreateDirectory(modules);
        try
        {
            string assembly = typeof(FactAttribute).Assembly.Location;
            string name = Path.GetFileName(assembly);
            File.Copy(assembly, Path.Combine(modules, name));
            File.WriteAllText(
                Path.Combine(modules, "modules.list"),
                name + (format == "crlf" ? "\r\n" : "\n"),
                new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: format == "bom"));
            File.WriteAllText(Path.Combine(modules, ".complete"), RuntimeBundleLayout.ModuleCompletionMarker);
            using var loader = new ModuleLoader(root);

            Assert.Throws<InvalidDataException>(() => loader.LoadClientModules());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void LegacyShadowCopy_PreservesNestedRuntimeAssets()
    {
        string root = Path.Combine(Path.GetTempPath(), "KarpikLegacyModuleCopy", Guid.NewGuid().ToString("N"));
        string source = Path.Combine(root, "source");
        string destination = Path.Combine(root, "destination");
        string nested = Path.Combine(source, "runtimes", "test", "native", "library.bin");
        Directory.CreateDirectory(Path.GetDirectoryName(nested)!);
        File.WriteAllText(nested, "native");
        Directory.CreateDirectory(destination);
        try
        {
            var copy = typeof(ModuleLoader).GetMethod(
                "CopyDirectory",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

            Assert.NotNull(copy);
            copy.Invoke(null, [source, destination, null]);

            Assert.Equal("native", File.ReadAllText(Path.Combine(destination, "runtimes", "test", "native", "library.bin")));
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
