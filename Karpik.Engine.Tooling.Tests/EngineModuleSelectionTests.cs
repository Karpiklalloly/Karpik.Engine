using System.Text;
using Karpik.Engine.Tooling;
using Xunit;

namespace Karpik.Engine.Tooling.Tests;

public sealed class EngineModuleSelectionTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ResolverHonorsExplicitlyDisabledDependencies(bool optional)
    {
        EngineModuleCatalogEntry[] catalog =
        [
            new("Consumer", "Consumer", EngineModuleKind.Standalone, EngineModuleSide.Shared, null,
                [new EngineModuleDependency("Disabled", optional)]),
            new("Disabled", "Disabled", EngineModuleKind.Standalone, EngineModuleSide.Shared, null, [])
        ];
        EngineModuleSelection[] selections =
        [new("Consumer", true, null), new("Disabled", false, null)];
        if (optional)
        {
            Assert.Equal(["Consumer"], EngineModuleSelectionResolver.Resolve(catalog, selections, EngineModuleSide.Shared)
                .Select(entry => entry.ModuleId));
        }
        else
        {
            var error = Assert.Throws<EngineModuleSelectionException>(() =>
                EngineModuleSelectionResolver.Resolve(catalog, selections, EngineModuleSide.Shared));
            Assert.Equal(EngineModuleSelectionErrorCode.MissingRequiredDependency, error.Code);
        }
    }

    [Fact]
    public void ExtendedCatalogRoundTripsCanonicalMetadata()
    {
        EngineModuleCatalogEntry[] entries =
        [
            new("Graphics.OpenGL", "Graphics", EngineModuleKind.Implementation, EngineModuleSide.Client, "OpenGL",
                [new EngineModuleDependency("Graphics.Core", false), new EngineModuleDependency("Optional.Ui", true)]),
            new("ECS.Core", "ECS", EngineModuleKind.Core, EngineModuleSide.Shared, null, [])
        ];

        string text = EngineModuleCatalog.Serialize(entries);
        EngineModuleCatalogEntry[] parsed = EngineModuleCatalog.Parse(Encoding.UTF8.GetBytes(text));

        Assert.Equal(
            "v2\nShared\tECS.Core\tECS\tCore\t\t\nClient\tGraphics.OpenGL\tGraphics\tImplementation\tOpenGL\tGraphics.Core,?Optional.Ui\n",
            text);
        Assert.Equal(["ECS.Core", "Graphics.OpenGL"], parsed.Select(entry => entry.ModuleId));
        Assert.Equal(entries[0].Dependencies, parsed[1].Dependencies);
    }

    [Fact]
    public void ResolverSelectsImplementationAndClosesRequiredDependencies()
    {
        EngineModuleCatalogEntry[] catalog =
        [
            new("ECS.Core", "ECS", EngineModuleKind.Core, EngineModuleSide.Shared, null, []),
            new("Graphics.Core", "Graphics", EngineModuleKind.Core, EngineModuleSide.Client, null,
                [new EngineModuleDependency("ECS.Core", false)]),
            new("Graphics.OpenGL", "Graphics", EngineModuleKind.Implementation, EngineModuleSide.Client, "OpenGL",
                [new EngineModuleDependency("Graphics.Core", false), new EngineModuleDependency("Optional.Ui", true)]),
            new("Graphics.Vulkan", "Graphics", EngineModuleKind.Implementation, EngineModuleSide.Client, "Vulkan", [])
        ];

        EngineModuleCatalogEntry[] selected = EngineModuleSelectionResolver.Resolve(
            catalog,
            [new EngineModuleSelection("Graphics", true, "OpenGL")],
            EngineModuleSide.Client);

        Assert.Equal(["ECS.Core", "Graphics.Core", "Graphics.OpenGL"], selected.Select(entry => entry.ModuleId));
    }

    [Fact]
    public void ResolverLeavesUnlistedLogicalModulesDisabledAndRejectsSideLeaks()
    {
        EngineModuleCatalogEntry[] catalog =
        [
            new("ECS.Core", "ECS", EngineModuleKind.Core, EngineModuleSide.Shared, null, []),
            new("Client.Ui", "Ui", EngineModuleKind.Standalone, EngineModuleSide.Client, null, []),
            new("Server.AI", "Ai", EngineModuleKind.Standalone, EngineModuleSide.Server, null, [])
        ];

        EngineModuleCatalogEntry[] selected = EngineModuleSelectionResolver.Resolve(
            catalog,
            [new EngineModuleSelection("ECS", true, null)],
            EngineModuleSide.Client);

        Assert.Equal(["ECS.Core"], selected.Select(entry => entry.ModuleId));
        EngineModuleSelectionException exception = Assert.Throws<EngineModuleSelectionException>(() =>
            EngineModuleSelectionResolver.Resolve(catalog, [new EngineModuleSelection("Ai", true, null)], EngineModuleSide.Client));
        Assert.Equal(EngineModuleSelectionErrorCode.SideLeak, exception.Code);
    }

    [Fact]
    public void ResolverReportsSelectionMetadataMissingForLegacyCatalog()
    {
        EngineModuleSelectionException exception = Assert.Throws<EngineModuleSelectionException>(() =>
            EngineModuleSelectionResolver.Resolve(
                [new EngineModuleCatalogEntry("ECS.Core", EngineModuleSide.Shared)],
                [new EngineModuleSelection("ECS", true, null)],
                EngineModuleSide.Client));

        Assert.Equal(EngineModuleSelectionErrorCode.SelectionMetadataMissing, exception.Code);
    }

    [Fact]
    public void ResolverRejectsInvalidImplementationAndMissingRequiredDependency()
    {
        EngineModuleCatalogEntry[] catalog =
        [
            new("Graphics.Core", "Graphics", EngineModuleKind.Core, EngineModuleSide.Client, null,
                [new EngineModuleDependency("Missing.Core", false)]),
            new("Graphics.OpenGL", "Graphics", EngineModuleKind.Implementation, EngineModuleSide.Client, "OpenGL", [])
        ];

        EngineModuleSelectionException invalidImplementation = Assert.Throws<EngineModuleSelectionException>(() =>
            EngineModuleSelectionResolver.Resolve(catalog, [new EngineModuleSelection("Graphics", true, "Vulkan")], EngineModuleSide.Client));
        Assert.Equal(EngineModuleSelectionErrorCode.InvalidImplementation, invalidImplementation.Code);

        EngineModuleSelectionException missingDependency = Assert.Throws<EngineModuleSelectionException>(() =>
            EngineModuleSelectionResolver.Resolve(catalog, [new EngineModuleSelection("Graphics", true, "OpenGL")], EngineModuleSide.Client));
        Assert.Equal(EngineModuleSelectionErrorCode.MissingRequiredDependency, missingDependency.Code);
    }
}
