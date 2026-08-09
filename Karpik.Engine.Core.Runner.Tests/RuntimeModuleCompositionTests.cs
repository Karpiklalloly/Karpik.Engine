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
                Directory.CreateDirectory(Path.Combine(modules, entry.ModuleId));
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
