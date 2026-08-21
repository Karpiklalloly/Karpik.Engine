using System.Text.RegularExpressions;
using Karpik.Engine.Core.Codegen;
using Microsoft.CodeAnalysis;
using Xunit;

namespace Karpik.Engine.Core.Generator.Tests;

public sealed class RuntimeCompositionGeneratorTests
{
    private const string InvalidPropertiesDiagnostic = "KCORE001";
    private const string InvalidInstallerDiagnostic = "KCORE002";
    private const string DuplicateIdentityDiagnostic = "KCORE003";
    private const string AmbiguousImplementationDiagnostic = "KCORE004";

    private static RuntimeCompositionGenerator CreateGenerator() => new();

    [Fact]
    public void Run_WithoutBuildProperties_EmitsNoSource()
    {
        var result = GeneratorTestHarness.Run(
            CreateGenerator(),
            side: null,
            projectKind: null,
            compositionMode: null);

        Assert.False(result.HasCompositionSource);
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void Run_DynamicMode_EmitsNoSource()
    {
        var result = GeneratorTestHarness.Run(
            CreateGenerator(),
            compositionMode: "Dynamic",
            additionalReferences: [GeneratorTestHarness.AssemblyReference<Karpik.Engine.Core.IModuleInstaller>()]);

        Assert.False(result.HasCompositionSource);
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void Run_SharedSide_EmitsNoSource()
    {
        var result = GeneratorTestHarness.Run(
            CreateGenerator(),
            side: "Shared",
            additionalReferences: [GeneratorTestHarness.AssemblyReference<Karpik.Engine.Core.IModuleInstaller>()]);

        Assert.False(result.HasCompositionSource);
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void Run_InvalidBuildProperty_ReportsDiagnosticAndEmitsNoSource()
    {
        var result = GeneratorTestHarness.Run(
            CreateGenerator(),
            side: "NotASide",
            additionalReferences: [GeneratorTestHarness.AssemblyReference<Karpik.Engine.Core.IModuleInstaller>()]);

        Assert.False(result.HasCompositionSource);
        Assert.Single(result.DiagnosticsById(InvalidPropertiesDiagnostic));
    }

    [Fact]
    public void Run_ClientStatic_DiscoversInstallerFromReferencedAssembly_AndEmitsDirectConstruction()
    {
        var moduleReference = GeneratorTestHarness.CompileModuleAssembly("RefModules", """
            using Karpik.Engine.Core;

            namespace Mods;

            [Module(ModuleScope.Simulation, 10)]
            public class RefInstaller : IModuleInstaller
            {
                public string Name => "Ref";
            }
            """);

        var result = GeneratorTestHarness.Run(
            CreateGenerator(),
            side: "Client",
            additionalReferences:
            [
                GeneratorTestHarness.AssemblyReference<Karpik.Engine.Core.IModuleInstaller>(),
                moduleReference,
            ]);

        result.AssertNoErrors();
        Assert.True(result.HasCompositionSource);
        Assert.Contains("namespace Karpik.Engine.Generated", result.CompositionSource, StringComparison.Ordinal);
        Assert.Contains("registry.Add(new global::Mods.RefInstaller())", result.CompositionSource, StringComparison.Ordinal);
        Assert.DoesNotContain("Activator", result.CompositionSource, StringComparison.Ordinal);
        Assert.DoesNotContain("GetType()", result.CompositionSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Run_ServerStatic_GeneratesComposition()
    {
        var moduleReference = ReferencedInstallerAssembly("RefModules");

        var result = GeneratorTestHarness.Run(
            CreateGenerator(),
            side: "Server",
            additionalReferences:
            [
                GeneratorTestHarness.AssemblyReference<Karpik.Engine.Core.IModuleInstaller>(),
                moduleReference,
            ]);

        result.AssertNoErrors();
        Assert.True(result.HasCompositionSource);
        Assert.Contains("global::Mods.RefInstaller", result.CompositionSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Run_DiscoversInstallersDefinedInCurrentCompilation()
    {
        var result = GeneratorTestHarness.Run(
            CreateGenerator(),
            source: """
                using Karpik.Engine.Core;

                namespace Host;

                [Module(ModuleScope.Engine)]
                public class HostInstaller : IModuleInstaller
                {
                    public string Name => "Host";
                }
                """,
            additionalReferences: [GeneratorTestHarness.AssemblyReference<Karpik.Engine.Core.IModuleInstaller>()]);

        result.AssertNoErrors();
        Assert.Contains("registry.Add(new global::Host.HostInstaller())", result.CompositionSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Run_SkipsInstallersWithoutModuleAttribute()
    {
        var result = GeneratorTestHarness.Run(
            CreateGenerator(),
            source: """
                using Karpik.Engine.Core;

                namespace Host;

                public class NotAModule : IModuleInstaller
                {
                    public string Name => "Nope";
                }
                """,
            additionalReferences: [GeneratorTestHarness.AssemblyReference<Karpik.Engine.Core.IModuleInstaller>()]);

        result.AssertNoErrors();
        Assert.True(result.HasCompositionSource);
        Assert.DoesNotContain("NotAModule", result.CompositionSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Run_OrdersByScopeThenPriorityThenAssemblyIdentityThenFullName()
    {
        var first = GeneratorTestHarness.CompileModuleAssembly("OrderM1", """
            using Karpik.Engine.Core;

            namespace M1;

            [Module(ModuleScope.Engine)]
            public class ZedInstaller : IModuleInstaller
            {
                public string Name => "Zed";
            }

            [Module(ModuleScope.Simulation)]
            public class AlphaInstaller : IModuleInstaller
            {
                public string Name => "Alpha";
            }
            """);
        var second = GeneratorTestHarness.CompileModuleAssembly("OrderM2", """
            using Karpik.Engine.Core;

            namespace M2;

            [Module(ModuleScope.ModSet, 5)]
            public class MidInstaller : IModuleInstaller
            {
                public string Name => "Mid";
            }

            [Module(ModuleScope.Engine)]
            public class AardvarkInstaller : IModuleInstaller
            {
                public string Name => "Aardvark";
            }
            """);

        var result = GeneratorTestHarness.Run(
            CreateGenerator(),
            source: """
                using Karpik.Engine.Core;

                namespace Host;

                [Module(ModuleScope.Engine, -100)]
                public class HostEarlyInstaller : IModuleInstaller
                {
                    public string Name => "HostEarly";
                }
                """,
            additionalReferences:
            [
                GeneratorTestHarness.AssemblyReference<Karpik.Engine.Core.IModuleInstaller>(),
                first,
                second,
            ]);

        result.AssertNoErrors();
        var registrations = Regex.Matches(result.CompositionSource, @"registry\.Add\(new global::([\w.]+)\(\)\);")
            .Select(match => match.Groups[1].Value)
            .ToArray();

        Assert.Equal(
            ["Host.HostEarlyInstaller", "M1.ZedInstaller", "M2.AardvarkInstaller", "M2.MidInstaller", "M1.AlphaInstaller"],
            registrations);
    }

    [Fact]
    public void Run_HonorsNamedPriorityArgument_WhenOrdering()
    {
        var first = GeneratorTestHarness.CompileModuleAssembly("NamedP1", """
            using Karpik.Engine.Core;

            namespace N1;

            [Module(ModuleScope.Engine, Priority = 5)]
            public class AaaInstaller : IModuleInstaller
            {
                public string Name => "Aaa";
            }
            """);
        var second = GeneratorTestHarness.CompileModuleAssembly("NamedP2", """
            using Karpik.Engine.Core;

            namespace N2;

            [Module(ModuleScope.Engine)]
            public class ZzzInstaller : IModuleInstaller
            {
                public string Name => "Zzz";
            }
            """);

        var result = GeneratorTestHarness.Run(
            CreateGenerator(),
            additionalReferences:
            [
                GeneratorTestHarness.AssemblyReference<Karpik.Engine.Core.IModuleInstaller>(),
                first,
                second,
            ]);

        result.AssertNoErrors();
        var registrations = Regex.Matches(result.CompositionSource, @"registry\.Add\(new global::([\w.]+)\(\)\);")
            .Select(match => match.Groups[1].Value)
            .ToArray();

        Assert.Equal(["N2.ZzzInstaller", "N1.AaaInstaller"], registrations);
    }

    [Fact]
    public void Run_EmptyGraph_EmitsCompilableEmptyComposition()
    {
        var result = GeneratorTestHarness.Run(
            CreateGenerator(),
            additionalReferences: [GeneratorTestHarness.AssemblyReference<Karpik.Engine.Core.IModuleInstaller>()]);

        result.AssertNoErrors();
        var loaded = GeneratorTestHarness.EmitAndLoad(result);
        var composition = LoadComposition(loaded);
        var registry = new RecordingRegistry();

        composition.RegisterModules(registry);

        Assert.Empty(registry.Installers);
    }

    [Fact]
    public void Run_GeneratedComposition_RegistersDirectInstancesWhenExecuted()
    {
        var module = ReferencedInstallerAssembly("ExecModules");
        var result = GeneratorTestHarness.Run(
            CreateGenerator(),
            side: "Client",
            additionalReferences:
            [
                GeneratorTestHarness.AssemblyReference<Karpik.Engine.Core.IModuleInstaller>(),
                module,
            ]);

        result.AssertNoErrors();
        var loaded = GeneratorTestHarness.EmitAndLoad(result, [module]);
        var composition = LoadComposition(loaded);
        var registry = new RecordingRegistry();

        composition.RegisterModules(registry);

        var installer = Assert.Single(registry.Installers);
        Assert.Equal("Mods.RefInstaller", installer.GetType().FullName);
    }

    [Theory]
    [InlineData("abstract")]
    [InlineData("internal")]
    public void Run_NonPublicOrAbstractInstaller_ReportsDiagnostic(string modifier)
    {
        var result = GeneratorTestHarness.Run(
            CreateGenerator(),
            source: $$"""
                using Karpik.Engine.Core;

                namespace Host;

                [Module(ModuleScope.Engine)]
                {{modifier}} class BadInstaller : IModuleInstaller
                {
                    public string Name => "Bad";
                }
                """,
            additionalReferences: [GeneratorTestHarness.AssemblyReference<Karpik.Engine.Core.IModuleInstaller>()]);

        Assert.Single(result.DiagnosticsById(InvalidInstallerDiagnostic));
        Assert.False(result.Compilation.GetDiagnostics().Any(static d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error && !d.Id.StartsWith("KCORE", StringComparison.Ordinal)));
    }

    [Fact]
    public void Run_OpenGenericInstaller_ReportsDiagnostic()
    {
        var result = GeneratorTestHarness.Run(
            CreateGenerator(),
            source: """
                using Karpik.Engine.Core;

                namespace Host;

                [Module(ModuleScope.Engine)]
                public class GenericInstaller<T> : IModuleInstaller
                {
                    public string Name => "Generic";
                }
                """,
            additionalReferences: [GeneratorTestHarness.AssemblyReference<Karpik.Engine.Core.IModuleInstaller>()]);

        Assert.Single(result.DiagnosticsById(InvalidInstallerDiagnostic));
    }

    [Fact]
    public void Run_InstallerWithoutPublicParameterlessConstructor_ReportsDiagnostic()
    {
        var result = GeneratorTestHarness.Run(
            CreateGenerator(),
            source: """
                using Karpik.Engine.Core;

                namespace Host;

                [Module(ModuleScope.Engine)]
                public class NoDefaultCtorInstaller : IModuleInstaller
                {
                    public NoDefaultCtorInstaller(int value) { }

                    public string Name => "NoCtor";
                }
                """,
            additionalReferences: [GeneratorTestHarness.AssemblyReference<Karpik.Engine.Core.IModuleInstaller>()]);

        Assert.Single(result.DiagnosticsById(InvalidInstallerDiagnostic));
    }

    [Fact]
    public void Run_DuplicateModuleIdentityAcrossAssemblies_ReportsDiagnostic()
    {
        var source = """
            using Karpik.Engine.Core;

            namespace Duplicated;

            [Module(ModuleScope.Engine)]
            public class SameNameInstaller : IModuleInstaller
            {
                public string Name => "Same";
            }
            """;
        var first = GeneratorTestHarness.CompileModuleAssembly("DupFirst", source);
        var second = GeneratorTestHarness.CompileModuleAssembly("DupSecond", source);

        var result = GeneratorTestHarness.Run(
            CreateGenerator(),
            additionalReferences:
            [
                GeneratorTestHarness.AssemblyReference<Karpik.Engine.Core.IModuleInstaller>(),
                first,
                second,
            ]);

        Assert.NotEmpty(result.DiagnosticsById(DuplicateIdentityDiagnostic));
    }

    [Fact]
    public void Run_BaseAndDerivedAttributedInstallers_ReportsAmbiguousImplementationDiagnostic()
    {
        var result = GeneratorTestHarness.Run(
            CreateGenerator(),
            additionalReferences:
            [
                GeneratorTestHarness.AssemblyReference<Karpik.Engine.Core.IModuleInstaller>(),
                GeneratorTestHarness.CompileModuleAssembly("HierarchyModules", """
                    using Karpik.Engine.Core;

                    namespace Hierarchy;

                    [Module(ModuleScope.Engine)]
                    public class BaseInstaller : IModuleInstaller
                    {
                        public string Name => "Base";
                    }

                    [Module(ModuleScope.Engine)]
                    public class DerivedInstaller : BaseInstaller
                    {
                        public string Name => "Derived";
                    }
                    """),
            ]);

        Assert.Single(result.DiagnosticsById(AmbiguousImplementationDiagnostic));
    }

    [Theory]
    [InlineData("Client")]
    [InlineData("Server")]
    public void GeneratedStaticRegistration_MatchesDynamicDiscovery_ModuleIdSets(string side)
    {
        var references = new[]
        {
            GeneratorTestHarness.AssemblyReference<Karpik.Engine.Core.IModuleInstaller>(),
            GeneratorTestHarness.AssemblyReference<Karpik.Engine.Shared.ECS.EcsModuleInstaller>(),
            GeneratorTestHarness.AssemblyReference<Karpik.Engine.Client.Graphics.Core.GraphicsCoreSimulationModuleInstaller>(),
            GeneratorTestHarness.AssemblyReference<Karpik.Engine.Client.Graphics.Core.GraphicsCoreEngineModuleInstaller>(),
            GeneratorTestHarness.AssemblyReference<Karpik.Engine.Shared.AssetManagement.Core.AssetManagementModuleInstaller>(),
        };

        var result = GeneratorTestHarness.Run(CreateGenerator(), side: side, additionalReferences: references);
        result.AssertNoErrors();
        var loaded = GeneratorTestHarness.EmitAndLoad(result);
        var composition = LoadComposition(loaded);
        var registry = new RecordingRegistry();
        composition.RegisterModules(registry);
        var staticIds = registry.Installers.Select(static installer => installer.GetType().FullName!).ToHashSet(StringComparer.Ordinal);

        var runner = new Karpik.Engine.Core.EngineRunner();
        runner.RegisterTypes(
        [
            typeof(Karpik.Engine.Shared.ECS.EcsModuleInstaller),
            typeof(Karpik.Engine.Client.Graphics.Core.GraphicsCoreSimulationModuleInstaller),
            typeof(Karpik.Engine.Client.Graphics.Core.GraphicsCoreEngineModuleInstaller),
            typeof(Karpik.Engine.Shared.AssetManagement.Core.AssetManagementModuleInstaller),
            typeof(NotAModuleType),
        ]);
        var dynamicIds = runner.GetModules().Select(static module => module.GetType().FullName!).ToHashSet(StringComparer.Ordinal);

        Assert.Equal(dynamicIds, staticIds);
    }

    private static IStaticRuntimeComposition LoadComposition(System.Reflection.Assembly assembly)
    {
        var type = assembly.GetType("Karpik.Engine.Generated.GeneratedRuntimeComposition")
                   ?? throw new InvalidOperationException("Generated composition type was not found.");
        return (IStaticRuntimeComposition)System.Activator.CreateInstance(type, nonPublic: true)!;
    }

    private static CompiledModule ReferencedInstallerAssembly(string assemblyName) =>
        GeneratorTestHarness.CompileModuleAssembly(assemblyName, """
            using Karpik.Engine.Core;

            namespace Mods;

            [Module(ModuleScope.Simulation, 10)]
            public class RefInstaller : IModuleInstaller
            {
                public string Name => "Ref";
            }
            """);

    private sealed class RecordingRegistry : IStaticModuleRegistry
    {
        public List<IModuleInstaller> Installers { get; } = [];

        public void Add(IModuleInstaller installer) => Installers.Add(installer);
    }

    private sealed class NotAModuleType;
}
