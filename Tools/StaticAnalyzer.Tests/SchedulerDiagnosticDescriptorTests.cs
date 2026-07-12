using Microsoft.CodeAnalysis;
using StaticAnalyzer;
using StaticAnalyzer.ECS;
using Xunit;

namespace StaticAnalyzer.Tests;

public sealed class SchedulerDiagnosticDescriptorTests
{
    [Fact]
    public void RenderPrepareCommandAttribute_AllowsInterfaceContracts()
    {
        var usage = Assert.Single(
            typeof(Karpik.Engine.Shared.ECS.Scheduling.RenderPrepareCommandAttribute)
                .GetCustomAttributes(typeof(AttributeUsageAttribute), inherit: false)
                .Cast<AttributeUsageAttribute>());

        Assert.True((usage.ValidOn & AttributeTargets.Interface) != 0);
    }

    [Fact]
    public void SchedulerDiagnosticIds_AreStableAndUnique()
    {
        string[] ids =
        [
            DiagnosticIds.EcsOpaqueUpdateAccess,
            DiagnosticIds.EcsAccessSummaryContradiction,
            DiagnosticIds.EcsInvalidOrderCycle,
            DiagnosticIds.EcsMainThreadOnlyUpdateAccess,
            DiagnosticIds.EcsUnsupportedManagedComponentSummary,
            DiagnosticIds.EcsGeneratedRegistryMissingSystem,
            DiagnosticIds.EcsRenderPrepareWriteAccess,
            DiagnosticIds.EcsUpdateRenderCommandAccess
        ];

        Assert.Equal(["K003", "K004", "K005", "K006", "K007", "K008", "K009", "K010"], ids);
        Assert.Equal(ids.Length, ids.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void SchedulerDiagnosticDescriptors_AreErrorsWithActionableMessages()
    {
        DiagnosticDescriptor[] descriptors =
        [
            SchedulerDiagnosticDescriptors.OpaqueUpdateAccess,
            SchedulerDiagnosticDescriptors.AccessSummaryContradiction,
            SchedulerDiagnosticDescriptors.InvalidOrderCycle,
            SchedulerDiagnosticDescriptors.MainThreadOnlyUpdateAccess,
            SchedulerDiagnosticDescriptors.UnsupportedManagedComponentSummary,
            SchedulerDiagnosticDescriptors.GeneratedRegistryMissingSystem,
            SchedulerDiagnosticDescriptors.RenderPrepareWriteAccess,
            SchedulerDiagnosticDescriptors.UpdateRenderCommandAccess
        ];

        foreach (DiagnosticDescriptor descriptor in descriptors)
        {
            Assert.Equal(DiagnosticSeverity.Error, descriptor.DefaultSeverity);
            Assert.Equal("ECS", descriptor.Category);
            Assert.True(descriptor.IsEnabledByDefault);
            Assert.Contains("{0}", descriptor.MessageFormat.ToString());
        }
    }
}
