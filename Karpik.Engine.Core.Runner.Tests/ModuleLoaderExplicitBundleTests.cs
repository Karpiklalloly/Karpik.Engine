using Karpik.Engine.Core;
using Karpik.Engine.Core.ModuleManagement;
using System.Composition;
using Xunit;

public sealed class ModuleLoaderExplicitBundleTests
{
    [Fact]
    public void PluginContext_IdentitySharesExportAttributeAssemblyWithRunner()
    {
        System.Reflection.Assembly contractAssembly = typeof(ExportAttribute).Assembly;
        string dependencyDirectory = Path.GetDirectoryName(contractAssembly.Location)!;
        var context = new PluginLoadContext(
            dependencyDirectory,
            dependencyDirectory,
            dependencyDirectories: [dependencyDirectory]);
        try
        {
            System.Reflection.Assembly loaded = context.LoadFromAssemblyName(contractAssembly.GetName());

            Assert.Same(contractAssembly, loaded);
        }
        finally
        {
            context.Unload();
        }
    }

    [Fact]
    public void ExplicitLoader_LoadsEngineAndGamePrimariesIntoOneCollectibleContext()
    {
        string root = Path.Combine(Path.GetTempPath(), "KarpikComposedLoader", Guid.NewGuid().ToString("N"));
        string modules = Path.Combine(root, "bundle", "modules.version.1");
        string engineModule = Path.Combine(root, "engine", "Karpik.Engine.Core.Runner.TestWorker");
        Directory.CreateDirectory(modules);
        Directory.CreateDirectory(engineModule);
        try
        {
            string gameSource = typeof(Karpik.Engine.Tooling.EngineModuleCatalog).Assembly.Location;
            string gameName = Path.GetFileName(gameSource);
            File.Copy(gameSource, Path.Combine(modules, gameName));
            File.WriteAllText(Path.Combine(modules, "modules.list"), gameName + "\n");
            File.WriteAllText(Path.Combine(modules, ".complete"), RuntimeBundleLayout.ModuleCompletionMarker);
            string engineSource = typeof(Karpik.Engine.Core.Runner.TestWorker.WorkerMarker).Assembly.Location;
            File.Copy(engineSource, Path.Combine(engineModule, Path.GetFileName(engineSource)));
            WeakReference lifetime = LoadComposedAndDispose(root, engineModule);
            for (int attempt = 0; attempt < 10 && lifetime.IsAlive; attempt++)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
            }
            Assert.False(lifetime.IsAlive);
            ModuleLoader.CleanupDisposedShadows();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static WeakReference LoadComposedAndDispose(string root, string engineModule)
    {
        var loader = new ModuleLoader(
            Path.Combine(root, "bundle"),
            [new ModuleLoader.EngineModuleDescriptor("Karpik.Engine.Core.Runner.TestWorker", engineModule)]);
        loader.LoadServerModules();
        Assert.Equal(2, loader.LoadedAssemblies.Length);
        var contexts = loader.LoadedAssemblies
            .Select(System.Runtime.Loader.AssemblyLoadContext.GetLoadContext)
            .Distinct()
            .ToArray();
        Assert.Single(contexts);
        Assert.True(contexts[0]!.IsCollectible);
        loader.Dispose();
        return Assert.IsType<WeakReference>(loader.LoadContextLifetime);
    }

    [Fact]
    public void PluginContext_RejectsTheOnlyCandidateWhenItsAssemblyIdentityDoesNotMatch()
    {
        string root = Path.Combine(Path.GetTempPath(), "KarpikAssemblyIdentity", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string source = typeof(Karpik.Engine.Core.Runner.TestWorker.WorkerMarker).Assembly.Location;
            File.Copy(source, Path.Combine(root, Path.GetFileName(source)));
            var context = new PluginLoadContext(root, root, dependencyDirectories: [root]);
            var requested = new System.Reflection.AssemblyName(
                "Karpik.Engine.Core.Runner.TestWorker, Version=999.0.0.0, Culture=neutral, PublicKeyToken=null");

            Assert.Throws<FileLoadException>(() => context.LoadFromAssemblyName(requested));
            context.Unload();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void PluginContext_ProbesCanonicalEngineNativeRootForCurrentRid()
    {
        string root = Path.Combine(Path.GetTempPath(), "KarpikNativeProbe", Guid.NewGuid().ToString("N"));
        string native = Path.Combine(root, "native");
        Directory.CreateDirectory(native);
        try
        {
            var context = new PluginLoadContext(root, root, nativeDirectories: [native]);

            string[] candidates = context.NativeCandidatePaths("karpik_test").ToArray();

            Assert.Contains(Path.Combine(native, "karpik_test"), candidates);
            Assert.Contains(candidates, path => path.StartsWith(
                Path.Combine(native, "runtimes", System.Runtime.InteropServices.RuntimeInformation.RuntimeIdentifier, "native"),
                StringComparison.OrdinalIgnoreCase));
            if (OperatingSystem.IsWindows())
                Assert.Contains(Path.Combine(native, "karpik_test.dll"), candidates);
            else
                Assert.Contains(candidates, path => path.EndsWith(OperatingSystem.IsMacOS() ? "libkarpik_test.dylib" : "libkarpik_test.so", StringComparison.Ordinal));
            context.Unload();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ExplicitLoader_RejectsGameOwnedCopyOfIdentitySharedCoreAssembly()
    {
        string root = Path.Combine(Path.GetTempPath(), "KarpikSharedAssembly", Guid.NewGuid().ToString("N"));
        string modules = Path.Combine(root, "modules.version.1");
        Directory.CreateDirectory(modules);
        try
        {
            string source = typeof(RuntimeLaunchOptions).Assembly.Location;
            string name = Path.GetFileName(source);
            File.Copy(source, Path.Combine(modules, name));
            File.WriteAllText(Path.Combine(modules, "modules.list"), name + "\n");
            File.WriteAllText(Path.Combine(modules, ".complete"), RuntimeBundleLayout.ModuleCompletionMarker);
            using var loader = new ModuleLoader(root);

            InvalidDataException exception = Assert.Throws<InvalidDataException>(() => loader.LoadServerModules());

            Assert.Contains("identity-share", exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

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

    [Fact]
    public void ExplicitLoader_CopyFailureRemovesNewShadowDirectory()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        string root = Path.Combine(Path.GetTempPath(), "KarpikModuleLoaderCopyFailure", Guid.NewGuid().ToString("N"));
        string modules = Path.Combine(root, "modules.version.1");
        Directory.CreateDirectory(modules);
        try
        {
            string name = "Locked.Client.dll";
            string assembly = Path.Combine(modules, name);
            File.WriteAllText(assembly, "locked");
            File.WriteAllText(Path.Combine(modules, "modules.list"), name + "\n");
            File.WriteAllText(Path.Combine(modules, ".complete"), RuntimeBundleLayout.ModuleCompletionMarker);
            using var locked = File.Open(assembly, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            using var loader = new ModuleLoader(root);

            Assert.Throws<IOException>(() => loader.LoadPluginCollection(["Locked.Client"]));

            Assert.NotEmpty(loader.ShadowCopyDirectory);
            Assert.False(Directory.Exists(loader.ShadowCopyDirectory));
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
