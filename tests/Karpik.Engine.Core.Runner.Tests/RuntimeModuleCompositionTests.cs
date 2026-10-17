using Karpik.Engine.Core;
using Karpik.Engine.Core.Runner;
using Karpik.Engine.Shared.ECS;
using Karpik.Engine.Tooling;
using Xunit;

public sealed class RuntimeModuleCompositionTests
{
    [Fact]
    public void Resolve_ServerIncludesSharedAndServerButNeverClientModules()
    {
        string root = Path.Combine(Path.GetTempPath(), "KarpikRuntimeComposition", Guid.NewGuid().ToString("N"));
        string modules = Path.Combine(root, "modules");
        Directory.CreateDirectory(modules);
        try
        {
            EngineModuleCatalogEntry[] entries =
            [
                new("ECS.Core", EngineModuleSide.Shared),
                new("Graphics.Core", EngineModuleSide.Client),
                new("Network.Server.Core", EngineModuleSide.Server)
            ];
            foreach (EngineModuleCatalogEntry entry in entries)
            {
                Directory.CreateDirectory(Path.Combine(modules, entry.ModuleId));
                File.WriteAllText(Path.Combine(modules, entry.ModuleId, entry.ModuleId + ".dll"), "module");
            }
            File.WriteAllText(Path.Combine(modules, EngineModuleCatalog.FileName), EngineModuleCatalog.Serialize(entries));

            ModuleLoader.EngineModuleDescriptor[] selected = RuntimeModuleComposition.Resolve(root, Side.Server);

            Assert.Equal(["ECS.Core", "Network.Server.Core"], selected.Select(module => module.ModuleId));
            Assert.DoesNotContain(selected, module => module.ModuleId == "Graphics.Core");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Resolve_SelectedDynamicManifestExcludesUnselectedAndOppositeSideModules()
    {
        string root = Path.Combine(Path.GetTempPath(), "KarpikRuntimeComposition", Guid.NewGuid().ToString("N"));
        string modules = Path.Combine(root, "modules");
        string bundleModules = Path.Combine(root, "bundle", "modules.version.1");
        Directory.CreateDirectory(modules);
        Directory.CreateDirectory(bundleModules);
        try
        {
            EngineModuleCatalogEntry[] entries =
            [
                new("ECS.Core", "ECS", EngineModuleKind.Core, EngineModuleSide.Shared, null, []),
                new("Server.AI", "AI", EngineModuleKind.Standalone, EngineModuleSide.Server, null, []),
                new("Client.Graphics", "Graphics", EngineModuleKind.Standalone, EngineModuleSide.Client, null, [])
            ];
            foreach (EngineModuleCatalogEntry entry in entries)
            {
                Directory.CreateDirectory(Path.Combine(modules, entry.ModuleId));
                File.WriteAllText(Path.Combine(modules, entry.ModuleId, entry.ModuleId + ".dll"), "module");
            }
            File.WriteAllText(Path.Combine(modules, EngineModuleCatalog.FileName), EngineModuleCatalog.Serialize(entries));
            File.WriteAllText(Path.Combine(root, "bundle", "runtime-bundle.side"), "karpik-runtime-side-v1:Server\n");
            File.WriteAllText(Path.Combine(root, "bundle", ".complete"), "karpik-runtime-bundle-v1\n");
            File.WriteAllText(Path.Combine(bundleModules, ".complete"), "karpik-module-staging-v1\n");
            File.WriteAllText(Path.Combine(bundleModules, "modules.list"), "Game.Server.dll\n");
            File.WriteAllText(Path.Combine(bundleModules, "Game.Server.dll"), "game");
            File.WriteAllText(Path.Combine(bundleModules, RuntimeBundleLayout.EngineModuleManifestFileName), "ECS.Core\nServer.AI\n");

            ModuleLoader.EngineModuleDescriptor[] selected = RuntimeModuleComposition.Resolve(
                root,
                Path.Combine(root, "bundle"),
                Side.Server);

            Assert.Equal(["ECS.Core", "Server.AI"], selected.Select(module => module.ModuleId));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Resolve_RejectsManifestWithoutRequiredImplementation()
    {
        string root = Path.Combine(Path.GetTempPath(), "KarpikRuntimeComposition", Guid.NewGuid().ToString("N"));
        string modules = Path.Combine(root, "modules");
        string bundleModules = Path.Combine(root, "bundle", "modules.version.1");
        Directory.CreateDirectory(modules);
        Directory.CreateDirectory(bundleModules);
        try
        {
            EngineModuleCatalogEntry[] entries =
            [
                new("ECS.Core", "ECS", EngineModuleKind.Core, EngineModuleSide.Shared, null, []),
                new("Graphics.Core", "Graphics", EngineModuleKind.Core, EngineModuleSide.Client, null, []),
                new("Graphics.OpenGL", "Graphics", EngineModuleKind.Implementation, EngineModuleSide.Client, "OpenGL", [])
            ];
            foreach (EngineModuleCatalogEntry entry in entries)
            {
                Directory.CreateDirectory(Path.Combine(modules, entry.ModuleId));
                File.WriteAllText(Path.Combine(modules, entry.ModuleId, entry.ModuleId + ".dll"), "module");
            }
            File.WriteAllText(Path.Combine(modules, EngineModuleCatalog.FileName), EngineModuleCatalog.Serialize(entries));
            File.WriteAllText(Path.Combine(root, "bundle", "runtime-bundle.side"), "karpik-runtime-side-v1:Client\n");
            File.WriteAllText(Path.Combine(root, "bundle", ".complete"), "karpik-runtime-bundle-v1\n");
            File.WriteAllText(Path.Combine(bundleModules, ".complete"), "karpik-module-staging-v1\n");
            File.WriteAllText(Path.Combine(bundleModules, "modules.list"), "Game.Client.dll\n");
            File.WriteAllText(Path.Combine(bundleModules, "Game.Client.dll"), "game");
            File.WriteAllText(Path.Combine(bundleModules, RuntimeBundleLayout.EngineModuleManifestFileName), "ECS.Core\nGraphics.Core\n");

            InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
                RuntimeModuleComposition.Resolve(root, Path.Combine(root, "bundle"), Side.Client));

            Assert.Contains("exactly one implementation", exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ValidateRequiredInstallers_RejectsCompositionWithoutEcsInstaller()
    {
        InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
            RuntimeModuleComposition.ValidateRequiredInstallers([typeof(RuntimeModuleCompositionTests)]));

        Assert.Contains("ECS.Core module installer", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ValidateRequiredInstallers_AcceptsCurrentEcsCoreInstaller()
    {
        RuntimeModuleComposition.ValidateRequiredInstallers([typeof(EcsModuleInstaller)]);
    }
}
