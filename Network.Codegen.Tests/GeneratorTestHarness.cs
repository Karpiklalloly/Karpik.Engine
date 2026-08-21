using System.Collections.Immutable;
using System.Reflection;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Xunit;

namespace Network.Codegen.Tests;

internal static class GeneratorTestHarness
{
    internal static GeneratorResult Run(
        IIncrementalGenerator generator,
        string source = "",
        string assemblyName = "AnyGame.Shared",
        string side = "Shared",
        string projectKind = "Runtime",
        string compositionMode = "Static",
        IEnumerable<MetadataReference>? additionalReferences = null)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source, CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Latest));
        var compilation = CSharpCompilation.Create(
            assemblyName,
            [syntaxTree],
            FrameworkReferences.Concat(additionalReferences ?? []),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true));

        var options = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["build_property.KarpikSide"] = side,
            ["build_property.KarpikProjectKind"] = projectKind,
            ["build_property.KarpikCompositionMode"] = compositionMode,
        };
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            [generator.AsSourceGenerator()],
            parseOptions: (CSharpParseOptions)syntaxTree.Options,
            optionsProvider: new TestAnalyzerConfigOptionsProvider(options));

        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var updatedCompilation, out var driverDiagnostics);
        var runResult = driver.GetRunResult();
        var generated = runResult.Results
            .SelectMany(static result => result.GeneratedSources)
            .ToDictionary(static sourceResult => sourceResult.HintName, static sourceResult => sourceResult.SourceText.ToString(), StringComparer.Ordinal);
        var diagnostics = driverDiagnostics
            .Concat(updatedCompilation.GetDiagnostics())
            .ToImmutableArray();

        return new GeneratorResult(updatedCompilation, generated, diagnostics);
    }

    internal static MetadataReference AssemblyReference<T>() => MetadataReference.CreateFromFile(typeof(T).Assembly.Location);

    internal static MetadataReference CompileReference(string assemblyName, string source)
    {
        var compilation = CSharpCompilation.Create(
            assemblyName,
            [CSharpSyntaxTree.ParseText(source, CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Latest))],
            FrameworkReferences.Concat([
                AssemblyReference<Karpik.Engine.Shared.Network.Core.NetworkedComponentAttribute>(),
                AssemblyReference<DCFApixels.DragonECS.IEcsComponent>(),
            ]),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var stream = new MemoryStream();
        var emit = compilation.Emit(stream);
        Assert.True(emit.Success, string.Join(Environment.NewLine, emit.Diagnostics));
        return MetadataReference.CreateFromImage(stream.ToArray());
    }

    internal static Assembly EmitAndLoad(GeneratorResult result)
    {
        using var stream = new MemoryStream();
        var emit = result.Compilation.Emit(stream);
        Assert.True(emit.Success, string.Join(Environment.NewLine, emit.Diagnostics));
        stream.Position = 0;
        return AssemblyLoadContext.Default.LoadFromStream(stream);
    }

    internal static IReadOnlyList<MetadataReference> RealSnapshotReferences =>
    [
        AssemblyReference<Karpik.Engine.Shared.Spatial2D.Transform2D>(),
        AssemblyReference<Karpik.Engine.Shared.Network.Core.NetworkedComponentAttribute>(),
        AssemblyReference<DCFApixels.DragonECS.EcsWorld>(),
        AssemblyReference<OpenTK.Mathematics.Vector2d>(),
    ];

    private static readonly ImmutableArray<MetadataReference> FrameworkReferences =
        ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))!
        .Split(Path.PathSeparator)
        .Where(static path => path.StartsWith(System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory(), StringComparison.OrdinalIgnoreCase))
        .Select(static path => (MetadataReference)MetadataReference.CreateFromFile(path))
        .ToImmutableArray();

    private sealed class TestAnalyzerConfigOptionsProvider(IReadOnlyDictionary<string, string> values)
        : AnalyzerConfigOptionsProvider
    {
        private readonly AnalyzerConfigOptions _global = new TestAnalyzerConfigOptions(values);
        public override AnalyzerConfigOptions GlobalOptions => _global;
        public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => EmptyAnalyzerConfigOptions.Instance;
        public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) => EmptyAnalyzerConfigOptions.Instance;
    }

    private sealed class TestAnalyzerConfigOptions(IReadOnlyDictionary<string, string> values) : AnalyzerConfigOptions
    {
        public override bool TryGetValue(string key, out string value) => values.TryGetValue(key, out value!);
    }

    private sealed class EmptyAnalyzerConfigOptions : AnalyzerConfigOptions
    {
        public static readonly EmptyAnalyzerConfigOptions Instance = new();
        public override bool TryGetValue(string key, out string value)
        {
            value = string.Empty;
            return false;
        }
    }
}

internal sealed record GeneratorResult(
    Compilation Compilation,
    IReadOnlyDictionary<string, string> GeneratedSources,
    ImmutableArray<Diagnostic> Diagnostics)
{
    public string SnapshotSource => GeneratedSources["NetworkSnapshotRegistry.g.cs"];

    public void AssertNoErrors()
    {
        var errors = Diagnostics.Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error).ToArray();
        Assert.Empty(errors);
    }
}
