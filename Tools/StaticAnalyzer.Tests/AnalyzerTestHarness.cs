using System.Collections.Immutable;
using DCFApixels.DragonECS;
using Karpik.Engine.Core;
using Karpik.Engine.Shared.ECS.Scheduling;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Xunit;

namespace StaticAnalyzer.Tests;

internal static class AnalyzerTestHarness
{
    private static readonly MetadataReference[] DefaultReferences = CreateReferences();

    public static async Task<ImmutableArray<Diagnostic>> GetAnalyzerDiagnosticsAsync(
        DiagnosticAnalyzer analyzer,
        string source,
        params string[] metadataReferenceSources)
    {
        MetadataReference[] references = DefaultReferences
            .Concat(metadataReferenceSources.Select(CreateMetadataReference))
            .ToArray();

        var compilation = CSharpCompilation.Create(
            assemblyName: "AnalyzerTestAssembly",
            syntaxTrees: [CSharpSyntaxTree.ParseText(source)],
            references: references,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true));

        Diagnostic[] compilationErrors = compilation
            .GetDiagnostics()
            .Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .ToArray();

        Assert.Empty(compilationErrors);

        CompilationWithAnalyzers compilationWithAnalyzers = compilation.WithAnalyzers([analyzer]);
        return await compilationWithAnalyzers.GetAnalyzerDiagnosticsAsync();
    }

    private static MetadataReference CreateMetadataReference(string source)
    {
        var compilation = CSharpCompilation.Create(
            assemblyName: "AnalyzerMetadataReference",
            syntaxTrees: [CSharpSyntaxTree.ParseText(source)],
            references: DefaultReferences,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true));

        Diagnostic[] compilationErrors = compilation
            .GetDiagnostics()
            .Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .ToArray();

        Assert.Empty(compilationErrors);

        using var stream = new MemoryStream();
        var result = compilation.Emit(stream);

        Assert.True(
            result.Success,
            string.Join(Environment.NewLine, result.Diagnostics.Select(static diagnostic => diagnostic.ToString())));

        return MetadataReference.CreateFromImage(stream.ToArray());
    }

    private static MetadataReference[] CreateReferences()
    {
        string[] trustedPlatformAssemblies = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator);

        MetadataReference[] platformReferences = trustedPlatformAssemblies
            .Select(static path => MetadataReference.CreateFromFile(path))
            .ToArray();

        MetadataReference[] projectReferences =
        [
            MetadataReference.CreateFromFile(typeof(ISystemUpdate).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(IEcsComponent).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(SequentialSystemAttribute).Assembly.Location)
        ];

        return platformReferences.Concat(projectReferences).ToArray();
    }
}
