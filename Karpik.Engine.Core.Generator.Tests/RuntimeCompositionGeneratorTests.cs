using System.Reflection;
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
    private const string MissingExportDiagnostic = "KE304";
    private const string InvalidServiceImplementationDiagnostic = "KE305";
    private const string AmbiguousConstructorDiagnostic = "KE306";
    private const string UnresolvableDependencyDiagnostic = "KE307";

    private static RuntimeCompositionGenerator CreateGenerator() => new();

    [Fact]
    public void GeneratedStaticComposition_EmitsAotComponentTemplateRoots()
    {
        // NativeAOT cannot compile MakeGenericType results at runtime; the
        // generated composition must statically touch one ComponentTemplate<T> /
        // TagComponentTemplate<T> instantiation per discovered ECS component so
        // the hot-reload state pipeline keeps working under AOT.
        var references = new[]
        {
            GeneratorTestHarness.AssemblyReference<Karpik.Engine.Core.IModuleInstaller>(),
            GeneratorTestHarness.AssemblyReference<DCFApixels.DragonECS.IEcsComponent>(),
            GeneratorTestHarness.AssemblyReference<Karpik.Engine.Shared.ECS.ComponentTemplateBase>(),
        };

        var result = GeneratorTestHarness.Run(
            CreateGenerator(),
            source: """
                using DCFApixels.DragonECS;

                public struct ProbeComponent : IEcsComponent
                {
                    public int Value;
                }

                public struct ProbeTagComponent : IEcsTagComponent { }

                [Karpik.Engine.Core.Module(Karpik.Engine.Core.ModuleScope.Simulation)]
                public sealed class ProbeInstaller : Karpik.Engine.Core.IModuleInstaller
                {
                    public string Name => nameof(ProbeInstaller);
                    public Karpik.Engine.Core.IModule CreateModule() => null!;
                }
                """,
            side: "Server",
            additionalReferences: references);

        Assert.True(result.HasCompositionSource);
        Assert.Contains(
            "typeof(global::Karpik.Engine.Shared.ECS.ComponentTemplate<global::ProbeComponent>)",
            result.CompositionSource,
            StringComparison.Ordinal);
        Assert.Contains(
            "typeof(global::Karpik.Engine.Shared.ECS.TagComponentTemplate<global::ProbeTagComponent>)",
            result.CompositionSource,
            StringComparison.Ordinal);
    }

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
    public void Run_SameReferencedIdentityButChangedModuleSource_RegeneratesFromFreshScan()
    {
        // The referenced-assembly scan is memoized across generations; its cache
        // key must include the REFERENCED module's source content, not only its
        // assembly identity. Editing a module's sources keeps its identity string
        // unchanged, so an identity-only key would serve stale models on the
        // next host compilation within one IDE session.
        const string moduleSourceV1 = """
            using Karpik.Engine.Core;

            namespace FpMods;

            public interface IProbe { }

            [System.Composition.Export(typeof(IProbe))]
            [ServiceRegistration(ModuleScope.Engine)]
            public class ProbeService : IProbe { }
            """;
        const string moduleSourceV2 = """
            using Karpik.Engine.Core;

            namespace FpMods;

            public interface IProbe { }

            [System.Composition.Export(typeof(IProbe))]
            [ServiceRegistration(ModuleScope.ModSet)]
            public class ProbeService : IProbe { }
            """;
        var exportReference = GeneratorTestHarness.AssemblyReference<System.Composition.ExportAttribute>();

        GeneratorResult first = GeneratorTestHarness.Run(
            CreateGenerator(),
            additionalReferences:
            [
                GeneratorTestHarness.AssemblyReference<Karpik.Engine.Core.IModuleInstaller>(),
                exportReference,
                GeneratorTestHarness.CreateModuleCompilation("FingerprintModules", moduleSourceV1, [exportReference]).ToMetadataReference(),
            ]);

        first.AssertNoErrors();
        Assert.Contains(
            "registry.Register<global::FpMods.IProbe, global::FpMods.ProbeService>(" +
            "global::Karpik.Engine.Core.ModuleScope.Engine, ",
            first.CompositionSource,
            StringComparison.Ordinal);

        GeneratorResult second = GeneratorTestHarness.Run(
            CreateGenerator(),
            additionalReferences:
            [
                GeneratorTestHarness.AssemblyReference<Karpik.Engine.Core.IModuleInstaller>(),
                exportReference,
                GeneratorTestHarness.CreateModuleCompilation("FingerprintModules", moduleSourceV2, [exportReference]).ToMetadataReference(),
            ]);

        second.AssertNoErrors();
        Assert.Contains(
            "registry.Register<global::FpMods.IProbe, global::FpMods.ProbeService>(" +
            "global::Karpik.Engine.Core.ModuleScope.ModSet, ",
            second.CompositionSource,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Run_SamePeReferencePathButRebuiltBinary_RegeneratesFromFreshScan()
    {
        // Production hosts reference modules as PortableExecutableReference.
        // The referenced-scan cache key must include the binary's file
        // fingerprint (path + last write time + length): a rebuilt DLL keeps
        // its assembly identity, so an identity-only key serves the previous
        // generation's models from the compiler server cache.
        const string moduleSourceV1 = """
            using Karpik.Engine.Core;

            namespace PeMods;

            public interface IProbe { }

            [System.Composition.Export(typeof(IProbe))]
            [ServiceRegistration(ModuleScope.Engine)]
            public class ProbeService : IProbe { }
            """;
        const string moduleSourceV2 = """
            using Karpik.Engine.Core;

            namespace PeMods;

            public interface IProbe { }

            [System.Composition.Export(typeof(IProbe))]
            [ServiceRegistration(ModuleScope.ModSet)]
            public class ProbeService : IProbe
            {
                public int ExtraMember => 42;
            }
            """;
        var exportReference = GeneratorTestHarness.AssemblyReference<System.Composition.ExportAttribute>();
        string directory = Path.Combine(Path.GetTempPath(), "PeFingerprint_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string dllPath = Path.Combine(directory, "PeFingerprintModules.dll");

        try
        {
            File.WriteAllBytes(
                dllPath,
                GeneratorTestHarness.CompileModuleAssembly("PeFingerprintModules", moduleSourceV1, [exportReference]).Image);

            GeneratorResult first = GeneratorTestHarness.Run(
                CreateGenerator(),
                additionalReferences:
                [
                    GeneratorTestHarness.AssemblyReference<Karpik.Engine.Core.IModuleInstaller>(),
                    exportReference,
                    MetadataReference.CreateFromFile(dllPath),
                ]);

            first.AssertNoErrors();
            Assert.Contains(
                "registry.Register<global::PeMods.IProbe, global::PeMods.ProbeService>(" +
                "global::Karpik.Engine.Core.ModuleScope.Engine, ",
                first.CompositionSource,
                StringComparison.Ordinal);

            // Rebuild in place: same path, same identity, different content.
            File.WriteAllBytes(
                dllPath,
                GeneratorTestHarness.CompileModuleAssembly("PeFingerprintModules", moduleSourceV2, [exportReference]).Image);

            GeneratorResult second = GeneratorTestHarness.Run(
                CreateGenerator(),
                additionalReferences:
                [
                    GeneratorTestHarness.AssemblyReference<Karpik.Engine.Core.IModuleInstaller>(),
                    exportReference,
                    MetadataReference.CreateFromFile(dllPath),
                ]);

            second.AssertNoErrors();
            Assert.Contains(
                "registry.Register<global::PeMods.IProbe, global::PeMods.ProbeService>(" +
                "global::Karpik.Engine.Core.ModuleScope.ModSet, ",
                second.CompositionSource,
                StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
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

    /// <summary>
    /// Dynamic↔Static parity (acceptance 10, Milestone 9 tightened): for one
    /// selection, the generated static service registrations must EQUAL the full
    /// runtime attribute-discovery set ([ServiceRegistration] + [Export] plus
    /// public ECS systems) - same implementations, scopes, lifetimes, export
    /// contracts, no Dynamic-only exceptions - and the emission sequence must
    /// match the canonical (scope, assembly, implementation) ordering.
    /// </summary>
    [Theory]
    [InlineData("Client")]
    [InlineData("Server")]
    public void GeneratedStaticRegistration_MatchesAttributeDiscovery_ServiceAndSystemSets(string side)
    {
        var result = GeneratorTestHarness.Run(
            CreateGenerator(),
            side: side,
            additionalReferences: CreateFullSelectionReferences(side));

        Assert.True(result.HasCompositionSource);

        var generatedRegistrations = new HashSet<string>(StringComparer.Ordinal);
        var generatedContractsByImpl = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        var generatedSequence = new List<(int ScopeRank, string Impl, string Lifetime)>();
        foreach (Match match in Regex.Matches(
                     result.CompositionSource,
                     @"registry\.Register<global::(?<contract>[^,]+), global::(?<impl>[^>]+)>\(\s*global::Karpik\.Engine\.Core\.ModuleScope\.(?<scope>\w+),\s*global::Karpik\.Engine\.Core\.ServiceLifetime\.(?<lifetime>\w+)"))
        {
            string impl = match.Groups["impl"].Value;
            int scopeRank = match.Groups["scope"].Value switch
            {
                "Engine" => 0,
                "ModSet" => 1,
                _ => 2,
            };
            generatedRegistrations.Add($"{impl}|{match.Groups["scope"].Value}|{match.Groups["lifetime"].Value}");
            generatedSequence.Add((scopeRank, impl, match.Groups["lifetime"].Value));
            if (!generatedContractsByImpl.TryGetValue(impl, out HashSet<string>? contracts))
            {
                contracts = [];
                generatedContractsByImpl.Add(impl, contracts);
            }
            contracts.Add(match.Groups["contract"].Value);
        }

        Assert.NotEmpty(generatedRegistrations);

        var dynamicRegistrations = new HashSet<string>(StringComparer.Ordinal);
        var dynamicContractsByImpl = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        var dynamicSequence = new List<(int ScopeRank, string Assembly, string Impl, string Lifetime)>();
        foreach (Type type in GetParityAssemblies(side).SelectMany(static assembly => assembly.GetTypes()))
        {
            Karpik.Engine.Core.ServiceRegistrationAttribute? registration =
                type.GetCustomAttribute<Karpik.Engine.Core.ServiceRegistrationAttribute>();
            bool isSystem = type.GetInterfaces().Any(static implemented =>
                implemented.Namespace == "Karpik.Engine.Core" &&
                implemented.Name.StartsWith("ISystem", StringComparison.Ordinal));
            if (registration is null && !isSystem)
            {
                continue;
            }

            if (!type.IsClass)
            {
                continue;
            }

            // Full-parity rule (Milestone 9): every attributed service and every
            // ECS system must be visible from the host launcher assembly.
            // Internal implementations are eliminated by publicization at the
            // source - never silently skipped.
            if (type.IsAbstract || type.IsGenericTypeDefinition || !type.IsVisible)
            {
                throw new InvalidOperationException(
                    $"Non-visible attributed candidate {type.FullName} breaks the full Dynamic/Static parity contract.");
            }

            string scope = registration?.Scope.ToString() ?? "Simulation";
            string lifetime = registration?.Lifetime.ToString() ?? "Transient";
            dynamicRegistrations.Add($"{type.FullName}|{scope}|{lifetime}");
            int scopeRank = scope switch
            {
                "Engine" => 0,
                "ModSet" => 1,
                _ => 2,
            };
            dynamicSequence.Add((scopeRank, type.Assembly.GetName().FullName ?? string.Empty, type.FullName!, lifetime));
            Type systemType = type;
            var exportedContracts = new HashSet<string>(StringComparer.Ordinal);
            foreach (System.Composition.ExportAttribute export in systemType.GetCustomAttributes<System.Composition.ExportAttribute>())
            {
                exportedContracts.Add(export.ContractType?.FullName ?? systemType.FullName!);
            }
            dynamicContractsByImpl[type.FullName!] = exportedContracts;
        }

        // Full SET equality in both directions - no documented deviations.
        Assert.Empty(generatedRegistrations.Except(dynamicRegistrations).OrderBy(static entry => entry, StringComparer.Ordinal));
        Assert.Empty(dynamicRegistrations.Except(generatedRegistrations).OrderBy(static entry => entry, StringComparer.Ordinal));

        // Full SEQUENCE equality (second audit, finding 4): the RAW emitted
        // order must equal the canonical Dynamic discovery order
        // (scope asc -> assembly identity -> implementation). One
        // implementation with several export contracts emits several Register
        // calls but is a single registration entry. The generated side is
        // never re-sorted here, so any drift between emission and the
        // production ServiceOrder/SystemOrder comparators fails fast.
        string[] dynamicOrdered = dynamicSequence
            .OrderBy(static entry => entry.ScopeRank)
            .ThenBy(static entry => entry.Assembly, StringComparer.Ordinal)
            .ThenBy(static entry => entry.Impl, StringComparer.Ordinal)
            .Select(static entry => $"{entry.Impl}|{entry.Lifetime}")
            .ToArray();
        string[] rawEmittedOrder = generatedSequence
            .GroupBy(static entry => $"{entry.Impl}|{entry.Lifetime}", StringComparer.Ordinal)
            .Select(static group => group.First())
            .Select(static entry => $"{entry.Impl}|{entry.Lifetime}")
            .ToArray();
        Assert.Equal(dynamicOrdered, rawEmittedOrder);

        foreach ((string impl, HashSet<string> contracts) in generatedContractsByImpl)
        {
            if (!dynamicContractsByImpl.TryGetValue(impl, out HashSet<string>? expected) || expected.Count == 0)
            {
                // System factories register only the concrete system type.
                Assert.Equal([impl], contracts.OrderBy(static entry => entry, StringComparer.Ordinal).ToArray());
                continue;
            }

            Assert.True(
                contracts.SetEquals(expected),
                $"Contract mismatch for {impl}: generated [{string.Join(", ", contracts.Order())}] vs exported [{string.Join(", ", expected.Order())}]");
        }
    }

    /// <summary>
    /// Full production module selections (second audit, finding 4): mirror
    /// Generated/ModuleLoader.cs ClientModules/ServerModules - the same module
    /// sets real template hosts reference via the SDK's
    /// _KarpikResolveStaticModuleReferences resolution. Anchor types provide
    /// one MetadataReference per production module assembly.
    /// </summary>
    private static readonly Type[] SharedSelectionAnchors =
    [
        typeof(DebugModule.DebugThings),
        typeof(Karpik.Engine.Shared.Log.LoggerModuleInstaller),
        typeof(Karpik.Engine.Shared.AssetManagement.Core.AssetManagementModuleInstaller),
        typeof(Karpik.Engine.Shared.Network.LiteNetLib.NetworkModuleInstaller),
        typeof(Karpik.Engine.Shared.ECS.EcsModuleInstaller),
        typeof(Karpik.Engine.Shared.Modding.ModdingModuleInstaller),
        typeof(Karpik.Engine.Shared.Modding.Lua.ModdingLuaModuleInstaller),
        typeof(Karpik.Engine.Shared.Network.LiteNetLib.LiteNetLibNetworkModuleInstaller),
        typeof(Karpik.Engine.Shared.Spatial2D.Transform2D),
        typeof(Karpik.Engine.Shared.Physics.Core.Physics2DModuleInstaller),
        typeof(Karpik.Engine.Shared.Physics.Aether2D.Physics2DAetherModuleInstaller),
        typeof(Karpik.Engine.Shared.StatAndAbilities.Buff),
        typeof(Karpik.Engine.Shared.Tweening.TweenModuleInstaller),
        typeof(Karpik.Engine.Shared.UnsafeUtilities.ResizableArray<>),
    ];

    private static readonly Dictionary<string, Type[]> SelectionAnchorsBySide = new()
    {
        ["Client"] =
        [
            ..SharedSelectionAnchors,
            typeof(Karpik.Engine.Client.Network.LiteNetLib.NetworkClientModuleInstaller),
            typeof(Karpik.Engine.Modules.Window.Core.WindowCoreModuleInstaller),
            typeof(Karpik.Engine.Client.Graphics.Core.GraphicsCoreSimulationModuleInstaller),
            typeof(Karpik.Engine.Client.Graphics.Core.GraphicsCoreEngineModuleInstaller),
            typeof(Karpik.Engine.Client.Graphics.OpenGL.GraphicsOpenGlEngineModuleInstaller),
            typeof(Karpik.Engine.Client.InputModule.InputModuleInstaller),
            typeof(Karpik.Engine.Modules.Window.Sdl2.WindowSdlModuleInstaller),
        ],
        ["Server"] =
        [
            ..SharedSelectionAnchors,
            typeof(Network.Server.LiteNetLib.NetworkServerModuleInstaller),
        ],
    };

    private static List<MetadataReference> CreateFullSelectionReferences(string side)
    {
        var references = new List<MetadataReference>
        {
            GeneratorTestHarness.AssemblyReference<Karpik.Engine.Core.IModuleInstaller>(),
            GeneratorTestHarness.AssemblyReference<System.Composition.ExportAttribute>(),
        };
        foreach (Type anchor in SelectionAnchorsBySide[side])
        {
            references.Add(GeneratorTestHarness.AssemblyReference(anchor));
        }

        return references;
    }

    private static IEnumerable<Assembly> GetParityAssemblies(string side) =>
        SelectionAnchorsBySide[side]
            .Select(static type => type.Assembly)
            .Distinct();

    /// <summary>
    /// End-to-end ordered Dynamic↔Static parity (second audit, finding 1): the
    /// DYNAMIC runner's module sequence after Sort must equal the GENERATED
    /// emitted installer sequence exactly - same canonical contract
    /// (Scope asc -> Priority asc -> assembly identity -> full name). The
    /// selection spans multiple scopes with equal priorities, where a
    /// Priority-first dynamic comparator diverges from the generator.
    /// </summary>
    [Fact]
    public void DynamicRunner_ModuleSequenceAfterSort_MatchesGeneratedEmission()
    {
        var references = new[]
        {
            GeneratorTestHarness.AssemblyReference<Karpik.Engine.Core.IModuleInstaller>(),
            GeneratorTestHarness.AssemblyReference<Karpik.Engine.Shared.ECS.EcsModuleInstaller>(),
            GeneratorTestHarness.AssemblyReference<Karpik.Engine.Client.Graphics.Core.GraphicsCoreSimulationModuleInstaller>(),
            GeneratorTestHarness.AssemblyReference<Karpik.Engine.Client.Graphics.Core.GraphicsCoreEngineModuleInstaller>(),
            GeneratorTestHarness.AssemblyReference<Karpik.Engine.Shared.AssetManagement.Core.AssetManagementModuleInstaller>(),
            GeneratorTestHarness.AssemblyReference<Karpik.Engine.Shared.Log.LoggerModuleInstaller>(),
            GeneratorTestHarness.AssemblyReference<Karpik.Engine.Shared.Modding.ModdingModuleInstaller>(),
        };

        var result = GeneratorTestHarness.Run(CreateGenerator(), side: "Client", additionalReferences: references);
        Assert.Empty(result.Diagnostics.Where(static diagnostic => diagnostic.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error && diagnostic.Id.StartsWith("KCORE", StringComparison.Ordinal)));

        string[] generatedSequence = Regex.Matches(result.CompositionSource, @"registry\.Add\(new global::([\w.]+)\(\)\);")
            .Select(match => match.Groups[1].Value)
            .ToArray();
        Assert.NotEmpty(generatedSequence);

        var runner = new Karpik.Engine.Core.EngineRunner();
        // Register in an order deliberately different from the canonical one so
        // the comparator - not insertion order or load rank - decides the result.
        runner.RegisterTypes(
        [
            typeof(Karpik.Engine.Shared.ECS.EcsModuleInstaller),
            typeof(Karpik.Engine.Shared.Modding.ModdingModuleInstaller),
            typeof(Karpik.Engine.Client.Graphics.Core.GraphicsCoreSimulationModuleInstaller),
            typeof(Karpik.Engine.Client.Graphics.Core.GraphicsCoreEngineModuleInstaller),
            typeof(Karpik.Engine.Shared.AssetManagement.Core.AssetManagementModuleInstaller),
            typeof(Karpik.Engine.Shared.Log.LoggerModuleInstaller),
            typeof(NotAModuleType),
        ]);
        runner.SortModulesForVerification();

        Assert.Equal(
            generatedSequence,
            runner.GetModules().Select(static module => module.GetType().FullName!).ToArray());
    }

    [Theory]
    [InlineData("Client")]
    [InlineData("Server")]
    public void GeneratedStaticRegistration_PreservesGeneratedInstallerSequenceThroughRunner(string side)
    {
        var references = CreateFullSelectionReferences(side);

        var result = GeneratorTestHarness.Run(CreateGenerator(), side: side, additionalReferences: references);
        Assert.Empty(result.Diagnostics.Where(static diagnostic => diagnostic.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error && diagnostic.Id.StartsWith("KCORE", StringComparison.Ordinal)));

        // The generated emission sequence is the canonical Static registration
        // order: Scope asc -> Priority asc -> assembly identity -> full name.
        string[] generatedSequence = Regex.Matches(result.CompositionSource, @"registry\.Add\(new global::([\w.]+)\(\)\);")
            .Select(match => match.Groups[1].Value)
            .ToArray();
        Assert.NotEmpty(generatedSequence);

        // Registering through the static composition boundary must yield exactly
        // the generated sequence - EngineRunner keeps a real per-installer rank
        // (Scope asc -> Priority asc -> assembly identity -> full name) instead
        // of reordering by full type name.
        var runner = new Karpik.Engine.Core.EngineRunner();
        runner.RegisterStaticComposition(new SequencedComposition(
            [.. generatedSequence.Select(ResolveProductionInstaller)]));

        Assert.Equal(
            generatedSequence,
            runner.GetModules().Select(static module => module.GetType().FullName!).ToArray());
    }

    private static IModuleInstaller ResolveProductionInstaller(string fullName)
    {
        Type? installerType = SelectionAnchorsBySide.Values
            .SelectMany(static anchors => anchors)
            .Select(static anchor => anchor.Assembly)
            .Distinct()
            .SelectMany(SafeGetTypes)
            .FirstOrDefault(type => type.FullName == fullName && !type.IsAbstract);
        if (installerType is null || !typeof(IModuleInstaller).IsAssignableFrom(installerType))
        {
            throw new InvalidOperationException($"Unexpected installer in generated source: {fullName}");
        }

        return (IModuleInstaller)Activator.CreateInstance(installerType)!;
    }

    private static IEnumerable<Type> SafeGetTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException exception)
        {
            return exception.Types.Where(static type => type is not null).Select(static type => type!);
        }
    }

    private sealed class SequencedComposition(IModuleInstaller[] installers) : IStaticRuntimeComposition
    {
        public void RegisterModules(IStaticModuleRegistry registry)
        {
            foreach (IModuleInstaller installer in installers)
            {
                registry.Add(installer);
            }
        }

        public void RegisterServices(IStaticServiceRegistry registry) { }

        public void RegisterEcsRegistryProviders(IStaticEcsRegistryProviders registry) { }
    }

    [Theory]
    [InlineData("Client")]
    [InlineData("Server")]
    public void GeneratedStaticRegistration_MatchesDynamicDiscovery_ModuleIdSets(string side)
    {
        var references = CreateFullSelectionReferences(side);

        var result = GeneratorTestHarness.Run(CreateGenerator(), side: side, additionalReferences: references);
        // Real engine module assemblies still contain internal services and scalar
        // dependencies that Static mode rejects with KE304-KE307, and the harness
        // compilation does not carry every transitive reference a real host has;
        // this parity check therefore asserts on generator diagnostics and emitted
        // registration order rather than emitting the assembly.
        Assert.Empty(result.Diagnostics.Where(static diagnostic => diagnostic.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error && diagnostic.Id.StartsWith("KCORE", StringComparison.Ordinal)));

        var staticIds = Regex.Matches(result.CompositionSource, @"registry\.Add\(new global::([\w.]+)\(\)\);")
            .Select(match => match.Groups[1].Value)
            .ToHashSet(StringComparer.Ordinal);

        var runner = new Karpik.Engine.Core.EngineRunner();
        runner.RegisterTypes(
        [
            ..SelectionAnchorsBySide[side].Where(static type =>
                typeof(Karpik.Engine.Core.IModuleInstaller).IsAssignableFrom(type) &&
                type is { IsAbstract: false, IsGenericTypeDefinition: false } &&
                type.GetCustomAttribute<Karpik.Engine.Core.ModuleAttribute>() is not null),
            typeof(NotAModuleType),
        ]);
        var dynamicIds = runner.GetModules().Select(static module => module.GetType().FullName!).ToHashSet(StringComparer.Ordinal);

        Assert.Equal(dynamicIds, staticIds);
    }

    [Fact]
    public void Run_ClientStatic_EmitsTypedFactoryForAttributedService()
    {
        var result = GeneratorTestHarness.Run(
            CreateGenerator(),
            source: ServiceHostSource,
            additionalReferences:
            [
                GeneratorTestHarness.AssemblyReference<Karpik.Engine.Core.IModuleInstaller>(),
                GeneratorTestHarness.AssemblyReference<System.Composition.ExportAttribute>(),
            ]);

        result.AssertNoErrors();
        Assert.True(result.HasCompositionSource);
        Assert.Contains(
            "registry.Register<global::Host.IDep, global::Host.Dep>(" +
            "global::Karpik.Engine.Core.ModuleScope.Engine, " +
            "global::Karpik.Engine.Core.ServiceLifetime.Singleton, " +
            "static resolver => new global::Host.Dep())",
            result.CompositionSource,
            StringComparison.Ordinal);
        Assert.DoesNotContain("Activator", result.CompositionSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Run_ClientStatic_ResolvesConstructorDependenciesInDeclarationOrderViaTypedResolve()
    {
        var result = GeneratorTestHarness.Run(
            CreateGenerator(),
            source: ServiceHostSource,
            additionalReferences:
            [
                GeneratorTestHarness.AssemblyReference<Karpik.Engine.Core.IModuleInstaller>(),
                GeneratorTestHarness.AssemblyReference<System.Composition.ExportAttribute>(),
            ]);

        result.AssertNoErrors();
        int first = result.CompositionSource.IndexOf(
            "static resolver => new global::Host.Consumer(resolver.Resolve<global::Host.IDep>(), resolver.Resolve<global::Host.Dep>())",
            StringComparison.Ordinal);
        Assert.True(first >= 0);
    }

    [Fact]
    public void Run_ClientStatic_EmitsOneRegistrationPerExportContractOfSameImplementation()
    {
        var result = GeneratorTestHarness.Run(
            CreateGenerator(),
            source: ServiceHostSource,
            additionalReferences:
            [
                GeneratorTestHarness.AssemblyReference<Karpik.Engine.Core.IModuleInstaller>(),
                GeneratorTestHarness.AssemblyReference<System.Composition.ExportAttribute>(),
            ]);

        result.AssertNoErrors();
        Assert.Contains(
            "registry.Register<global::Host.IConsumer, global::Host.Consumer>(",
            result.CompositionSource,
            StringComparison.Ordinal);
        Assert.Contains(
            "registry.Register<global::Host.Consumer, global::Host.Consumer>(",
            result.CompositionSource,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Run_ClientStatic_HonorsExplicitTransientLifetime()
    {
        var result = GeneratorTestHarness.Run(
            CreateGenerator(),
            source: """
                using Karpik.Engine.Core;

                namespace Host;

                public interface IPerCall { }
                [System.Composition.Export(typeof(IPerCall))]
                [ServiceRegistration(ModuleScope.Simulation, ServiceLifetime.Transient)]
                public class PerCall : IPerCall { }
                """,
            additionalReferences:
            [
                GeneratorTestHarness.AssemblyReference<Karpik.Engine.Core.IModuleInstaller>(),
                GeneratorTestHarness.AssemblyReference<System.Composition.ExportAttribute>(),
            ]);

        result.AssertNoErrors();
        Assert.Contains(
            "registry.Register<global::Host.IPerCall, global::Host.PerCall>(" +
            "global::Karpik.Engine.Core.ModuleScope.Simulation, " +
            "global::Karpik.Engine.Core.ServiceLifetime.Transient, ",
            result.CompositionSource,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Run_ClientStatic_RegistersStartableContractForIStartableImplementations()
    {
        var result = GeneratorTestHarness.Run(
            CreateGenerator(),
            source: """
                using Autofac;
                using Karpik.Engine.Core;

                namespace Host;

                public interface IStartedService { }
                [System.Composition.Export(typeof(IStartedService))]
                [ServiceRegistration(ModuleScope.Engine)]
                public class StartedService : IStartedService, IStartable
                {
                    public void Start() { }
                }
                """,
            additionalReferences:
            [
                GeneratorTestHarness.AssemblyReference<Karpik.Engine.Core.IModuleInstaller>(),
                GeneratorTestHarness.AssemblyReference<System.Composition.ExportAttribute>(),
                GeneratorTestHarness.AssemblyReference<Autofac.IStartable>(),
            ]);

        result.AssertNoErrors();
        Assert.Contains(
            "registry.Register<global::Autofac.IStartable, global::Host.StartedService>(",
            result.CompositionSource,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Run_ServerStatic_RegistersEcsSystemsAsSimulationTransientSelfRegistrations()
    {
        var result = GeneratorTestHarness.Run(
            CreateGenerator(),
            side: "Server",
            source: """
                using Karpik.Engine.Core;

                namespace Host;

                public class ProbeSystem : ISystemInit
                {
                    public void Init() { }
                }
                """,
            additionalReferences:
            [
                GeneratorTestHarness.AssemblyReference<Karpik.Engine.Core.IModuleInstaller>(),
                GeneratorTestHarness.AssemblyReference<System.Composition.ExportAttribute>(),
            ]);

        result.AssertNoErrors();
        Assert.Contains(
            "registry.Register<global::Host.ProbeSystem, global::Host.ProbeSystem>(" +
            "global::Karpik.Engine.Core.ModuleScope.Simulation, " +
            "global::Karpik.Engine.Core.ServiceLifetime.Transient, " +
            "static resolver => new global::Host.ProbeSystem())",
            result.CompositionSource,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Run_ServerStatic_EmitsConcreteEcsRegistryProvidersViaCompositionContract()
    {
        var result = GeneratorTestHarness.Run(
            CreateGenerator(),
            side: "Server",
            source: """
                using Karpik.Engine.Core;

                namespace Host;

                public class ProbeUpdateSystem : ISystemUpdate
                {
                    public void Update() { }
                }

                public class ProbeSystem
                {
                    public void Init() { }
                }
                """,
            additionalReferences:
            [
                GeneratorTestHarness.AssemblyReference<Karpik.Engine.Core.IModuleInstaller>(),
                GeneratorTestHarness.AssemblyReference<System.Composition.ExportAttribute>(),
            ]);

        result.AssertNoErrors();
        string source = result.CompositionSource;
        // The composition hands over generated provider instances; the runner's
        // Static path never enumerates assemblies reflectively.
        Assert.Contains(
            "public void RegisterEcsRegistryProviders(global::Karpik.Engine.Core.IStaticEcsRegistryProviders registry)",
            source,
            StringComparison.Ordinal);
        Assert.Contains("registry.AddUpdate(new GeneratedEcsUpdateRegistryProvider());", source, StringComparison.Ordinal);
        Assert.DoesNotContain("registry.AddRenderPrepare(", source, StringComparison.Ordinal);

        Assert.Contains(
            "internal sealed class GeneratedEcsUpdateRegistryProvider : global::Karpik.Engine.Shared.ECS.Scheduling.IEcsUpdateRegistryProvider",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "new global::Karpik.Engine.Shared.ECS.Scheduling.EcsUpdateSystemDescriptor(typeof(global::Host.ProbeUpdateSystem), IsSequential: false, Host_ProbeUpdateSystemAccesses, Host_ProbeUpdateSystemOrders)",
            source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void GeneratedServiceFactories_ConstructInstancesWithResolvedDependenciesWhenExecuted()
    {
        var result = GeneratorTestHarness.Run(
            CreateGenerator(),
            source: ServiceHostSource,
            additionalReferences:
            [
                GeneratorTestHarness.AssemblyReference<Karpik.Engine.Core.IModuleInstaller>(),
                GeneratorTestHarness.AssemblyReference<System.Composition.ExportAttribute>(),
            ]);

        result.AssertNoErrors();
        var loaded = GeneratorTestHarness.EmitAndLoad(result);
        var composition = LoadComposition(loaded);
        var registry = new RecordingStaticRegistry();

        composition.RegisterServices(registry);

        var consumerRegistration = Assert.Single(registry.Registrations, static registration =>
            registration.ContractType!.FullName == "Host.IConsumer");
        Assert.Equal("Host.Consumer", consumerRegistration.ImplementationType.FullName);
        Assert.Equal(ModuleScope.ModSet, consumerRegistration.Scope);
        Assert.Equal(ServiceLifetime.Transient, consumerRegistration.Lifetime);

        Type depType = consumerRegistration.ImplementationType.Assembly.GetType("Host.Dep")!;
        var dep = Activator.CreateInstance(depType)!;
        var resolver = new StubResolver();
        Type idepType = consumerRegistration.ImplementationType.Assembly.GetType("Host.IDep")!;
        resolver.Add(idepType, dep);
        resolver.Add(depType, dep);
        object consumer = consumerRegistration.Factory(resolver);

        Assert.True(consumerRegistration.ContractType!.IsInstanceOfType(consumer));
        Assert.Same(dep, ((dynamic)consumer).Dependency);
    }

    [Fact]
    public void GeneratedServiceRegistrations_DedupeByImplementationAcrossContracts()
    {
        var result = GeneratorTestHarness.Run(
            CreateGenerator(),
            source: ServiceHostSource,
            additionalReferences:
            [
                GeneratorTestHarness.AssemblyReference<Karpik.Engine.Core.IModuleInstaller>(),
                GeneratorTestHarness.AssemblyReference<System.Composition.ExportAttribute>(),
            ]);

        result.AssertNoErrors();
        var loaded = GeneratorTestHarness.EmitAndLoad(result);
        var composition = LoadComposition(loaded);
        var registry = new RecordingStaticRegistry();
        composition.RegisterServices(registry);

        var consumerContracts = registry.Registrations
            .Where(static registration => registration.ImplementationType.FullName == "Host.Consumer")
            .Select(static registration => registration.ContractType!.FullName)
            .ToHashSet(StringComparer.Ordinal);

        Assert.Equal(new[] { "Host.Consumer", "Host.IConsumer" }, consumerContracts.OrderBy(static name => name, StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public void Run_ServiceWithoutExport_ReportsMissingExportDiagnostic()
    {
        var result = GeneratorTestHarness.Run(
            CreateGenerator(),
            source: """
                using Karpik.Engine.Core;

                namespace Host;

                [ServiceRegistration(ModuleScope.Engine)]
                public class NoExportService { }
                """,
            additionalReferences:
            [
                GeneratorTestHarness.AssemblyReference<Karpik.Engine.Core.IModuleInstaller>(),
                GeneratorTestHarness.AssemblyReference<System.Composition.ExportAttribute>(),
            ]);

        Assert.Single(result.DiagnosticsById(MissingExportDiagnostic));
    }

    [Fact]
    public void Run_AbstractServiceImplementation_ReportsInvalidImplementation()
    {
        var result = GeneratorTestHarness.Run(
            CreateGenerator(),
            source: """
                using Karpik.Engine.Core;

                namespace Host;

                public interface IService { }
                [System.Composition.Export(typeof(IService))]
                [ServiceRegistration(ModuleScope.Engine)]
                public abstract class BadService : IService { }
                """,
            additionalReferences:
            [
                GeneratorTestHarness.AssemblyReference<Karpik.Engine.Core.IModuleInstaller>(),
                GeneratorTestHarness.AssemblyReference<System.Composition.ExportAttribute>(),
            ]);

        Assert.Single(result.DiagnosticsById(InvalidServiceImplementationDiagnostic));
    }

    [Fact]
    public void Run_InternalServiceInHostAssembly_RemainsUsableByEmittedFactory()
    {
        var result = GeneratorTestHarness.Run(
            CreateGenerator(),
            source: """
                using Karpik.Engine.Core;

                namespace Host;

                public interface IService { }
                [System.Composition.Export(typeof(IService))]
                [ServiceRegistration(ModuleScope.Engine)]
                internal class InternalHostService : IService { }
                """,
            additionalReferences:
            [
                GeneratorTestHarness.AssemblyReference<Karpik.Engine.Core.IModuleInstaller>(),
                GeneratorTestHarness.AssemblyReference<System.Composition.ExportAttribute>(),
            ]);

        result.AssertNoErrors();
        Assert.Contains(
            "registry.Register<global::Host.IService, global::Host.InternalHostService>(",
            result.CompositionSource,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Run_OpenGenericServiceImplementation_ReportsInvalidImplementation()
    {
        var result = GeneratorTestHarness.Run(
            CreateGenerator(),
            source: """
                using Karpik.Engine.Core;

                namespace Host;

                public interface IService<T> { }
                [System.Composition.Export(typeof(IService<>))]
                [ServiceRegistration(ModuleScope.Engine)]
                public class GenericService<T> : IService<T> { }
                """,
            additionalReferences:
            [
                GeneratorTestHarness.AssemblyReference<Karpik.Engine.Core.IModuleInstaller>(),
                GeneratorTestHarness.AssemblyReference<System.Composition.ExportAttribute>(),
            ]);

        Assert.Single(result.DiagnosticsById(InvalidServiceImplementationDiagnostic));
    }

    [Fact]
    public void Run_InternalServiceFromReferencedAssembly_ReportsInvalidImplementation()
    {
        var moduleReference = GeneratorTestHarness.CompileModuleAssembly("InternalServices", """
            using Karpik.Engine.Core;

            namespace RefServices;

            public interface IHidden { }

            [System.Composition.Export(typeof(IHidden))]
            [ServiceRegistration(ModuleScope.Engine)]
            internal class Hidden : IHidden { }
            """, [GeneratorTestHarness.AssemblyReference<System.Composition.ExportAttribute>()]);
        var result = GeneratorTestHarness.Run(
            CreateGenerator(),
            additionalReferences:
            [
                GeneratorTestHarness.AssemblyReference<Karpik.Engine.Core.IModuleInstaller>(),
                GeneratorTestHarness.AssemblyReference<System.Composition.ExportAttribute>(),
                moduleReference,
            ]);

        Assert.Single(result.DiagnosticsById(InvalidServiceImplementationDiagnostic));
    }

    [Fact]
    public void Run_ServiceWithoutAccessibleConstructor_ReportsInvalidImplementation()
    {
        var result = GeneratorTestHarness.Run(
            CreateGenerator(),
            source: """
                using Karpik.Engine.Core;

                namespace Host;

                public interface IService { }
                [System.Composition.Export(typeof(IService))]
                [ServiceRegistration(ModuleScope.Engine)]
                public class PrivateCtorService : IService
                {
                    private PrivateCtorService() { }
                }
                """,
            additionalReferences:
            [
                GeneratorTestHarness.AssemblyReference<Karpik.Engine.Core.IModuleInstaller>(),
                GeneratorTestHarness.AssemblyReference<System.Composition.ExportAttribute>(),
            ]);

        Assert.Single(result.DiagnosticsById(InvalidServiceImplementationDiagnostic));
    }

    [Fact]
    public void Run_ServiceWithTwoPublicConstructors_ReportsAmbiguousConstructorDiagnostic()
    {
        var result = GeneratorTestHarness.Run(
            CreateGenerator(),
            source: """
                using Karpik.Engine.Core;

                namespace Host;

                public interface IService { }
                [System.Composition.Export(typeof(IService))]
                [ServiceRegistration(ModuleScope.Engine)]
                public class AmbiguousService : IService
                {
                    public AmbiguousService() { }
                    public AmbiguousService(int value) { }
                }
                """,
            additionalReferences:
            [
                GeneratorTestHarness.AssemblyReference<Karpik.Engine.Core.IModuleInstaller>(),
                GeneratorTestHarness.AssemblyReference<System.Composition.ExportAttribute>(),
            ]);

        Assert.Single(result.DiagnosticsById(AmbiguousConstructorDiagnostic));
    }

    [Fact]
    public void Run_ServiceWithScalarDependencyNotInClosedGraph_ReportsUnresolvableDependency()
    {
        var result = GeneratorTestHarness.Run(
            CreateGenerator(),
            source: """
                using Karpik.Engine.Core;

                namespace Host;

                public interface IService { }
                [System.Composition.Export(typeof(IService))]
                [ServiceRegistration(ModuleScope.Engine)]
                public class ScalarService : IService
                {
                    public ScalarService(int value) { }
                }
                """,
            additionalReferences:
            [
                GeneratorTestHarness.AssemblyReference<Karpik.Engine.Core.IModuleInstaller>(),
                GeneratorTestHarness.AssemblyReference<System.Composition.ExportAttribute>(),
            ]);

        Assert.Single(result.DiagnosticsById(UnresolvableDependencyDiagnostic));
    }

    [Fact]
    public void Run_ServiceWithUnknownConcreteDependency_GetsNoDiagnosticBecauseOnRegisterServicesIsInvisible()
    {
        var result = GeneratorTestHarness.Run(
            CreateGenerator(),
            source: """
                using Karpik.Engine.Core;

                namespace Host;

                public interface IService { }
                public sealed class UnknownRuntimeDependency { }
                [System.Composition.Export(typeof(IService))]
                [ServiceRegistration(ModuleScope.Engine)]
                public class Consumer : IService
                {
                    public Consumer(UnknownRuntimeDependency dependency) { }
                }
                """,
            additionalReferences:
            [
                GeneratorTestHarness.AssemblyReference<Karpik.Engine.Core.IModuleInstaller>(),
                GeneratorTestHarness.AssemblyReference<System.Composition.ExportAttribute>(),
            ]);

        result.AssertNoErrors();
    }

    internal const string ServiceHostSource = """
        using Karpik.Engine.Core;

        namespace Host;

        public interface IDep { }
        [System.Composition.Export(typeof(IDep))]
        [ServiceRegistration(ModuleScope.Engine)]
        public class Dep : IDep { }
        public interface IConsumer { }
        [System.Composition.Export(typeof(IConsumer))]
        [System.Composition.Export(typeof(Consumer))]
        [ServiceRegistration(ModuleScope.ModSet, ServiceLifetime.Transient)]
        public class Consumer : IConsumer
        {
            public IDep Dependency { get; }

            public Consumer(IDep dep, Dep concrete)
            {
                Dependency = dep;
                _ = concrete;
            }
        }
        """;

    private sealed class RecordingStaticRegistry : IStaticServiceRegistry
    {
        public List<RecordedServiceRegistration> Registrations { get; } = [];

        public void Register<TContract, TImplementation>(
            ModuleScope scope,
            ServiceLifetime lifetime,
            Func<IServiceResolver, TImplementation> factory)
            where TImplementation : class, TContract
        {
            Registrations.Add(new RecordedServiceRegistration(
                scope,
                lifetime,
                typeof(TContract),
                typeof(TImplementation),
                resolver => factory(resolver)));
        }
    }

    private sealed record RecordedServiceRegistration(
        ModuleScope Scope,
        ServiceLifetime Lifetime,
        Type? ContractType,
        Type ImplementationType,
        Func<IServiceResolver, object> Factory);

    private sealed class StubResolver : IServiceResolver
    {
        private readonly Dictionary<Type, object> _services = new();

        public void Add(Type type, object instance) => _services.Add(type, instance);

        public T Resolve<T>() where T : notnull => (T)_services[typeof(T)];

        public object Resolve(Type serviceType) => _services[serviceType];

        public IEnumerable<T> ResolveAll<T>() => throw new NotSupportedException();

        public object? GetService(Type serviceType) =>
            _services.TryGetValue(serviceType, out object? service) ? service : null;
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

