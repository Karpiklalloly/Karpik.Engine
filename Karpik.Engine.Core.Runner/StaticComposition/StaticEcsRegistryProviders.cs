using Karpik.Engine.Core;
using Karpik.Engine.Shared.ECS.Scheduling;

namespace Karpik.Engine.Core.Runner;

/// <summary>
/// Collects generated ECS registry providers handed over by the static
/// composition. Descriptor enumeration is a direct span concatenation of the
/// provided instances - no assembly scanning and no activation happens here.
/// </summary>
internal sealed class StaticEcsRegistryProviders : IStaticEcsRegistryProviders
{
    private readonly List<IEcsUpdateRegistryProvider> _update = [];
    private readonly List<IEcsRenderPrepareRegistryProvider> _renderPrepare = [];

    public void AddUpdate(IEcsUpdateRegistryProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        _update.Add(provider);
    }

    public void AddRenderPrepare(IEcsRenderPrepareRegistryProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        _renderPrepare.Add(provider);
    }

    public EcsUpdateSystemDescriptor[] CollectUpdateDescriptors()
    {
        var descriptors = new List<EcsUpdateSystemDescriptor>();
        foreach (IEcsUpdateRegistryProvider provider in _update)
        {
            Append(provider.GetUpdateSystems(), descriptors);
        }

        return descriptors.ToArray();
    }

    public EcsUpdateSystemDescriptor[] CollectRenderPrepareDescriptors()
    {
        var descriptors = new List<EcsUpdateSystemDescriptor>();
        foreach (IEcsRenderPrepareRegistryProvider provider in _renderPrepare)
        {
            Append(provider.GetRenderPrepareSystems(), descriptors);
        }

        return descriptors.ToArray();
    }

    private static void Append(
        ReadOnlySpan<EcsUpdateSystemDescriptor> providerDescriptors,
        List<EcsUpdateSystemDescriptor> descriptors)
    {
        for (int i = 0; i < providerDescriptors.Length; i++)
        {
            descriptors.Add(providerDescriptors[i]);
        }
    }
}
