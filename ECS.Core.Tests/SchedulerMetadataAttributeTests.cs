using DCFApixels.DragonECS;
using Karpik.Engine.Core;
using Karpik.Engine.Shared.ECS.Scheduling;
using System.Reflection;
using Xunit;

public sealed class SchedulerMetadataAttributeTests
{
    [Fact]
    public void SequentialSystemAttribute_IsSingleUseClassMarker()
    {
        AttributeUsageAttribute usage = GetUsage<SequentialSystemAttribute>();

        Assert.Equal(AttributeTargets.Class, usage.ValidOn);
        Assert.False(usage.AllowMultiple);
        Assert.True(usage.Inherited);
    }

    [Fact]
    public void MainThreadOnlyAttribute_IsSingleUseClassOrMethodMarker()
    {
        AttributeUsageAttribute usage = GetUsage<MainThreadOnlyAttribute>();

        Assert.Equal(AttributeTargets.Class | AttributeTargets.Method, usage.ValidOn);
        Assert.False(usage.AllowMultiple);
        Assert.True(usage.Inherited);
    }

    [Fact]
    public void ReadsAndWritesAttributes_ExposeComponentTypesAndAccessModes()
    {
        EcsAccessAttribute[] attributes = typeof(AccessDecoratedSystem)
            .GetCustomAttributes(inherit: true)
            .OfType<EcsAccessAttribute>()
            .OrderBy(static attribute => attribute.ComponentType.Name)
            .ToArray();

        Assert.Collection(
            attributes,
            attribute =>
            {
                Assert.Equal(typeof(MetadataComponentA), attribute.ComponentType);
                Assert.Equal(EcsAccessMode.Read, attribute.Mode);
            },
            attribute =>
            {
                Assert.Equal(typeof(MetadataComponentB), attribute.ComponentType);
                Assert.Equal(EcsAccessMode.Write, attribute.Mode);
            });
    }

    [Fact]
    public void RunsBeforeAndAfterAttributes_ExposeTargetSystemTypesAndOrderKinds()
    {
        EcsOrderAttribute[] attributes = typeof(OrderDecoratedSystem)
            .GetCustomAttributes(inherit: true)
            .OfType<EcsOrderAttribute>()
            .OrderBy(static attribute => attribute.Kind)
            .ToArray();

        Assert.Collection(
            attributes,
            attribute =>
            {
                Assert.Equal(EcsOrderKind.After, attribute.Kind);
                Assert.Equal(typeof(PredecessorSystem), attribute.SystemType);
            },
            attribute =>
            {
                Assert.Equal(EcsOrderKind.Before, attribute.Kind);
                Assert.Equal(typeof(SuccessorSystem), attribute.SystemType);
            });
    }

    [Fact]
    public void AccessAndOrderAttributes_CanBeAppliedToHelperMethods()
    {
        var method = typeof(AccessDecoratedSystem).GetMethod(nameof(AccessDecoratedSystem.Helper))!;

        Assert.Contains(
            method.GetCustomAttributes(inherit: true).OfType<EcsAccessAttribute>(),
            attribute => attribute.ComponentType == typeof(MetadataComponentA) &&
                         attribute.Mode == EcsAccessMode.Read);

        Assert.Contains(
            method.GetCustomAttributes(inherit: true).OfType<EcsOrderAttribute>(),
            attribute => attribute.SystemType == typeof(PredecessorSystem) &&
                         attribute.Kind == EcsOrderKind.After);
    }

    private static AttributeUsageAttribute GetUsage<TAttribute>()
        where TAttribute : Attribute
    {
        return Assert.Single(typeof(TAttribute).GetCustomAttributes<AttributeUsageAttribute>());
    }

    [Reads<MetadataComponentA>]
    [Writes<MetadataComponentB>]
    private sealed class AccessDecoratedSystem : ISystemUpdate
    {
        public void Update()
        {
        }

        [Reads<MetadataComponentA>]
        [RunsAfter<PredecessorSystem>]
        public void Helper()
        {
        }
    }

    [RunsAfter<PredecessorSystem>]
    [RunsBefore<SuccessorSystem>]
    private sealed class OrderDecoratedSystem : ISystemUpdate
    {
        public void Update()
        {
        }
    }

    private sealed class PredecessorSystem : ISystemUpdate
    {
        public void Update()
        {
        }
    }

    private sealed class SuccessorSystem : ISystemUpdate
    {
        public void Update()
        {
        }
    }
}

internal readonly struct MetadataComponentA : IEcsComponent;
internal readonly struct MetadataComponentB : IEcsComponent;
