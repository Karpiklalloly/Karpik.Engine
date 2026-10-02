using System.Reflection;
using Karpik.Engine.Core;
using Karpik.Engine.Shared.ECS.Scheduling;

namespace Karpik.Engine.Core.Runner;

/// <summary>
/// Dynamic-composition-only descriptor discovery: enumerates assemblies with
/// <see cref="Assembly.GetTypes"/> and activates registry providers through
/// <see cref="Activator.CreateInstance"/>. The Static execution path never
/// enters this type - it receives generated providers through
/// <see cref="IStaticRuntimeComposition"/> instead (enforced by the
/// source-boundary tests).
/// </summary>
internal static class DynamicCompositionDiscovery
{
    internal static IModuleInstaller[] ActivateModuleInstallers(Type[] types)
    {
        var classTypes = types.Where(t => t.IsClass && !t.IsAbstract);
        var moduleTypes = classTypes.Where(t => typeof(IModuleInstaller).IsAssignableFrom(t) || typeof(IModuleInstaller).IsAssignableTo(t));
        var withAttr = moduleTypes.Where(t => t.GetCustomAttribute<ModuleAttribute>() != null);
        return withAttr
            .Select(type => (IModuleInstaller)Activator.CreateInstance(type)!)
            .ToArray();
    }

    internal static EcsUpdateSystemDescriptor[] CollectUpdateDescriptors(ReadOnlySpan<ISystemUpdate> systems)
    {
        if (systems.Length == 0)
        {
            return [];
        }

        var assemblies = new HashSet<Assembly>();
        for (int i = 0; i < systems.Length; i++)
        {
            assemblies.Add(systems[i].GetType().Assembly);
        }

        var descriptors = new List<EcsUpdateSystemDescriptor>();
        foreach (Assembly assembly in assemblies)
        {
            AddProviderDescriptors<IEcsUpdateRegistryProvider>(assembly, static provider => provider.GetUpdateSystems(), descriptors);
        }

        return descriptors.ToArray();
    }

    internal static EcsUpdateSystemDescriptor[] CollectRenderPrepareDescriptors(
        ReadOnlySpan<ISystemRenderPrepare> systems)
    {
        if (systems.Length == 0)
        {
            return [];
        }

        var assemblies = new HashSet<Assembly>();
        for (int i = 0; i < systems.Length; i++)
        {
            assemblies.Add(systems[i].GetType().Assembly);
        }

        var descriptors = new List<EcsUpdateSystemDescriptor>();
        foreach (Assembly assembly in assemblies)
        {
            AddProviderDescriptors<IEcsRenderPrepareRegistryProvider>(
                assembly,
                static provider => provider.GetRenderPrepareSystems(),
                descriptors);
        }

        return descriptors.ToArray();
    }

    private static void AddProviderDescriptors<TProvider>(
        Assembly assembly,
        Func<TProvider, ReadOnlySpan<EcsUpdateSystemDescriptor>> getDescriptors,
        List<EcsUpdateSystemDescriptor> descriptors)
        where TProvider : class
    {
        Type providerInterface = typeof(TProvider);
        foreach (Type type in assembly.GetTypes())
        {
            if (type.IsAbstract || !providerInterface.IsAssignableFrom(type))
            {
                continue;
            }

            var provider = (TProvider?)Activator.CreateInstance(type, nonPublic: true);
            if (provider is null)
            {
                throw new InvalidOperationException(
                    $"Unable to create ECS registry provider '{type.FullName}'.");
            }

            ReadOnlySpan<EcsUpdateSystemDescriptor> providerDescriptors = getDescriptors(provider);
            for (int i = 0; i < providerDescriptors.Length; i++)
            {
                descriptors.Add(providerDescriptors[i]);
            }
        }
    }
}
