using System.Collections.Immutable;
using DCFApixels.DragonECS;
using Karpik.Engine.Core;
using Karpik.Engine.Core.Codegen;
using Karpik.Engine.Shared.ECS.Scheduling;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace StaticAnalyzer.Tests;

public sealed class EcsUpdateRegistryGeneratorTests
{
    [Fact]
    public void GeneratesRegistryProviderForUpdateSystemsAndAccessMetadata()
    {
        string generatedSource = RunGenerator(
            """
            using DCFApixels.DragonECS;
            using Karpik.Engine.Core;
            using Karpik.Engine.Shared.ECS.Scheduling;

            [Reads<Position>]
            [Writes<Velocity>]
            [RunsAfter<SpawnSystem>]
            public sealed class MovementSystem : ISystemUpdate
            {
                public void Update()
                {
                }
            }

            [SequentialSystem]
            public sealed class SpawnSystem : ISystemUpdate
            {
                public void Update()
                {
                }
            }

            public struct Position : IEcsComponent
            {
                public int X;
            }

            public struct Velocity : IEcsComponent
            {
                public int X;
            }
            """);

        Assert.Contains("internal sealed class GeneratedEcsUpdateRegistryProvider", generatedSource);
        Assert.Contains("global::Karpik.Engine.Shared.ECS.Scheduling.IEcsUpdateRegistryProvider", generatedSource);
        Assert.Contains("typeof(global::MovementSystem)", generatedSource);
        Assert.Contains("typeof(global::SpawnSystem)", generatedSource);
        Assert.Contains("typeof(global::Position)", generatedSource);
        Assert.Contains("typeof(global::Velocity)", generatedSource);
        Assert.Contains("global::Karpik.Engine.Shared.ECS.Scheduling.EcsAccessMode.Read", generatedSource);
        Assert.Contains("global::Karpik.Engine.Shared.ECS.Scheduling.EcsAccessMode.Write", generatedSource);
        Assert.Contains("global::Karpik.Engine.Shared.ECS.Scheduling.EcsOrderKind.After", generatedSource);
        Assert.Contains("IsSequential: true", generatedSource);
    }

    private static string RunGenerator(string source)
    {
        string[] trustedPlatformAssemblies = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator);

        MetadataReference[] references = trustedPlatformAssemblies
            .Select(static path => MetadataReference.CreateFromFile(path))
            .Concat(
            [
                MetadataReference.CreateFromFile(typeof(ISystemUpdate).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(IEcsComponent).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(SequentialSystemAttribute).Assembly.Location)
            ])
            .ToArray();

        var compilation = CSharpCompilation.Create(
            assemblyName: "GeneratorTestAssembly",
            syntaxTrees: [CSharpSyntaxTree.ParseText(source)],
            references: references,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        GeneratorDriver driver = CSharpGeneratorDriver.Create([new EcsUpdateRegistryGenerator()]);
        driver = driver.RunGeneratorsAndUpdateCompilation(
            compilation,
            out Compilation outputCompilation,
            out ImmutableArray<Diagnostic> generatorDiagnostics);

        Assert.Empty(generatorDiagnostics.Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));
        Assert.Empty(outputCompilation.GetDiagnostics().Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));

        ImmutableArray<GeneratedSourceResult> generatedSources = driver.GetRunResult().GeneratedTrees
            .Select((tree, index) => new GeneratedSourceResult(index, tree.GetText().ToString()))
            .ToImmutableArray();

        GeneratedSourceResult generated = Assert.Single(
            generatedSources,
            result => result.Source.Contains("GeneratedEcsUpdateRegistryProvider"));

        return generated.Source;
    }

    private readonly record struct GeneratedSourceResult(int Index, string Source);
}
