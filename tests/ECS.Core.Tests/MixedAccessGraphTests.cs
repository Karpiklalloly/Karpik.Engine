using Karpik.Engine.Shared.ECS.Scheduling;
using Xunit;

public sealed class MixedAccessGraphTests
{
    private sealed class A;
    private sealed class B;

    [Fact]
    public void MixedKeys_DefaultDescriptorKeepsLegacyNullSentinel()
    {
        Assert.Null(default(EcsComponentAccessDescriptor).ComponentType);
    }

    [Fact]
    public void MixedKeys_InvalidModeReportsDynamicId()
    {
        var error = Assert.Throws<EcsUpdateGraphBuildException>(() => EcsUpdateGraphBuilder.BuildInstances([
            new(typeof(A), false, new[] { new EcsComponentAccessDescriptor(EcsComponentAccessKey.ForDynamic("weather:Temperature"), (EcsAccessMode)99) }, Array.Empty<EcsSystemOrderDescriptor>(), "a")]));
        Assert.Contains("weather:Temperature", error.Message);
    }

    [Fact]
    public void MixedKeys_EmptyAccessKeyRejectsMetadata()
    {
        Assert.Throws<EcsUpdateGraphBuildException>(() => EcsUpdateGraphBuilder.BuildInstances([
            new(typeof(A), false, new[] { default(EcsComponentAccessDescriptor) }, Array.Empty<EcsSystemOrderDescriptor>(), "a")]));
    }

    [Theory]
    [InlineData(false, EcsAccessMode.Read, false)]
    [InlineData(true, EcsAccessMode.Read, false)]
    [InlineData(false, EcsAccessMode.Write, true)]
    [InlineData(true, EcsAccessMode.Write, true)]
    public void MixedKeys_BothBuildPathsRespectDynamicReadWrite(bool instances, EcsAccessMode second, bool conflict)
    {
        var key = EcsComponentAccessKey.ForDynamic("weather:Temperature");
        EcsUpdateSystemDescriptor[] descriptors =
        [new(typeof(A), false, new[] { new EcsComponentAccessDescriptor(key, EcsAccessMode.Read) }, Array.Empty<EcsSystemOrderDescriptor>(), "a"),
         new(typeof(B), false, new[] { new EcsComponentAccessDescriptor(key, second) }, Array.Empty<EcsSystemOrderDescriptor>(), "b")];
        var graph = instances ? EcsUpdateGraphBuilder.BuildInstances(descriptors) : EcsUpdateGraphBuilder.Build([typeof(A), typeof(B)], descriptors);
        Assert.Equal(conflict ? new[] { 0 } : [], graph.Nodes.Span[1].DependencySystemIds.ToArray());
        Assert.Equal(new[] { key }, graph.AccessKeys.ToArray());
        Assert.Empty(graph.ComponentTypes.ToArray());
    }

    [Fact]
    public void MixedKeys_DistinctDynamicIdsAndClrIdentityDoNotCollide()
    {
        var first = EcsComponentAccessKey.ForDynamic("weather:Temperature");
        var second = EcsComponentAccessKey.ForDynamic("other:Temperature");
        Assert.NotEqual(EcsComponentAccessKey.ForClr(typeof(A)), EcsComponentAccessKey.ForDynamic(typeof(A).FullName!));
        var graph = EcsUpdateGraphBuilder.BuildInstances([
            new(typeof(A), false, new[] { new EcsComponentAccessDescriptor(first, EcsAccessMode.Write) }, Array.Empty<EcsSystemOrderDescriptor>(), "a"),
            new(typeof(B), false, new[] { new EcsComponentAccessDescriptor(second, EcsAccessMode.Write) }, Array.Empty<EcsSystemOrderDescriptor>(), "b")]);
        Assert.Empty(graph.Nodes.Span[1].DependencySystemIds.ToArray());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MixedKeys_WriteWriteConflictsOnBothBuildPaths(bool instances)
    {
        var key = EcsComponentAccessKey.ForDynamic("weather:Temperature");
        EcsUpdateSystemDescriptor[] descriptors =
        [new(typeof(A), false, new[] { new EcsComponentAccessDescriptor(key, EcsAccessMode.Write) }, Array.Empty<EcsSystemOrderDescriptor>(), "a"),
         new(typeof(B), false, new[] { new EcsComponentAccessDescriptor(key, EcsAccessMode.Write) }, Array.Empty<EcsSystemOrderDescriptor>(), "b")];
        var graph = instances ? EcsUpdateGraphBuilder.BuildInstances(descriptors) : EcsUpdateGraphBuilder.Build([typeof(A), typeof(B)], descriptors);
        Assert.Equal(new[] { 0 }, graph.Nodes.Span[1].DependencySystemIds.ToArray());
    }

    [Fact]
    public void MixedKeys_ClrCompatibilityAndWriteDominatesRead()
    {
        var clr = EcsComponentAccessKey.ForClr(typeof(A));
        var graph = EcsUpdateGraphBuilder.BuildInstances([
            new(typeof(A), false, new[] { new EcsComponentAccessDescriptor(typeof(A), EcsAccessMode.Read), new EcsComponentAccessDescriptor(clr, EcsAccessMode.Write) }, Array.Empty<EcsSystemOrderDescriptor>(), "a"),
            new(typeof(B), false, new[] { new EcsComponentAccessDescriptor(clr, EcsAccessMode.Read) }, Array.Empty<EcsSystemOrderDescriptor>(), "b")]);
        Assert.Equal(new[] { typeof(A) }, graph.ComponentTypes.ToArray());
        Assert.Equal(new[] { clr }, graph.AccessKeys.ToArray());
        Assert.Empty(graph.Nodes.Span[0].ReadComponentIds.ToArray());
        Assert.Single(graph.Nodes.Span[0].WriteComponentIds.ToArray());
        Assert.Equal(new[] { 0 }, graph.Nodes.Span[1].DependencySystemIds.ToArray());
    }
}
