using Karpik.Engine.Tooling;

namespace Karpik.Engine.Core.Runner;

public static class RuntimeModuleComposition
{
    public static ModuleLoader.EngineModuleDescriptor[] Resolve(string engineRoot, Side side)
    {
        EngineModuleSide moduleSide = side switch
        {
            Side.Client => EngineModuleSide.Client,
            Side.Server => EngineModuleSide.Server,
            _ => throw new ArgumentOutOfRangeException(nameof(side), side, "Only Client and Server runtimes are supported.")
        };
        string modulesRoot = Path.Combine(engineRoot, "modules");
        EngineModuleCatalogEntry[] selected = EngineModuleCatalog.ForSide(
            EngineModuleCatalog.Read(modulesRoot),
            moduleSide);
        if (!selected.Any(entry => entry is { Side: EngineModuleSide.Shared, ModuleId: "ECS.Core" }))
        {
            throw new InvalidDataException("The installed engine runtime must provide shared module ECS.Core.");
        }
        return selected.Select(entry => new ModuleLoader.EngineModuleDescriptor(
                entry.ModuleId,
                Path.Combine(modulesRoot, entry.ModuleId)))
            .ToArray();
    }

    public static void ValidateRequiredInstallers(IEnumerable<Type> types)
    {
        ArgumentNullException.ThrowIfNull(types);
        bool hasEcsInstaller = types.Any(type =>
            type.Assembly.GetName().Name == "ECS.Core" &&
            type.Name == "ECSInstaller" &&
            !type.IsAbstract &&
            typeof(IInstaller).IsAssignableFrom(type) &&
            type.GetCustomAttributes(typeof(ModuleAttribute), inherit: false).Length == 1);
        if (!hasEcsInstaller)
        {
            throw new InvalidDataException("Engine runtime composition is missing the ECS.Core ECSInstaller.");
        }
    }
}
