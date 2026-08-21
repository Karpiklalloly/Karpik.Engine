using System.Collections.Immutable;
using System.Reflection;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Xunit;

namespace Karpik.Engine.Core.Generator.Tests;

internal static class GeneratorTestHarness
{
    internal static GeneratorResult Run(
        IIncrementalGenerator generator,
        string source = "",
        string assemblyName = "AnyGame.Client.Host",
        string? side = "Client",
        string? projectKind = "Runtime",
        string? compositionMode = "Static",
        IEnumerable<MetadataReference>? additionalReferences = null)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source, CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Latest));
        var compilation = CSharpCompilation.Create(
            assemblyName,
            [syntaxTree],
            FrameworkReferences.Concat(additionalReferences ?? []),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true));

        var options = new Dictionary<string, string>(StringComparer.Ordinal);
        if (side is not null)
        {
            options["build_property.KarpikSide"] = side;
        }
        if (projectKind is not null)
        {
            options["build_property.KarpikProjectKind"] = projectKind;
        }
        if (compositionMode is not null)
        {
            options["build_property.KarpikCompositionMode"] = compositionMode;
        }

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
            .Where(static diagnostic => diagnostic.Severity >= DiagnosticSeverity.Warning)
            .ToImmutableArray();

        return new GeneratorResult(updatedCompilation, generated, diagnostics);
    }

    internal static MetadataReference AssemblyReference<T>() => MetadataReference.CreateFromFile(typeof(T).Assembly.Location);

    internal static CompiledModule CompileModuleAssembly(string assemblyName, string source)
    {
        var compilation = CSharpCompilation.Create(
            assemblyName,
            [CSharpSyntaxTree.ParseText(source, CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Latest))],
            FrameworkReferences.Concat([AssemblyReference<Karpik.Engine.Core.IModuleInstaller>()]),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var stream = new MemoryStream();
        var emit = compilation.Emit(stream);
        Assert.True(emit.Success, string.Join(Environment.NewLine, emit.Diagnostics));
        return new CompiledModule(
            MetadataReference.CreateFromImage(stream.ToArray()),
            stream.ToArray());
    }

    internal static Assembly EmitAndLoad(GeneratorResult result, IReadOnlyList<CompiledModule>? dependencies = null)
    {
        using var stream = new MemoryStream();
        var emit = result.Compilation.Emit(stream);
        Assert.True(emit.Success, string.Join(Environment.NewLine, emit.Diagnostics));
        stream.Position = 0;
        var loadContext = new AssemblyLoadContext($"generator-tests-{Guid.NewGuid():N}", isCollectible: true);
        if (dependencies is not null)
        {
            foreach (var dependency in dependencies)
            {
                loadContext.LoadFromDependency(dependency);
            }
        }
        return loadContext.LoadFromStream(stream);
    }

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

internal sealed record CompiledModule(MetadataReference Reference, byte[] Image)
{
    public static implicit operator MetadataReference(CompiledModule module) => module.Reference;
}

internal static class AssemblyLoadContextExtensions
{
    public static void LoadFromDependency(this AssemblyLoadContext context, CompiledModule module)
    {
        using var stream = new MemoryStream(module.Image);
        context.LoadFromStream(stream);
    }
}

internal sealed record GeneratorResult(
    Compilation Compilation,
    IReadOnlyDictionary<string, string> GeneratedSources,
    ImmutableArray<Diagnostic> Diagnostics)
{
    public const string CompositionHintName = "GeneratedRuntimeComposition.g.cs";

    public bool HasCompositionSource => GeneratedSources.ContainsKey(CompositionHintName);

    public string CompositionSource => GeneratedSources[CompositionHintName];

    public void AssertNoErrors()
    {
        var errors = Diagnostics.Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error).ToArray();
        Assert.Empty(errors);
    }

    public IReadOnlyList<Diagnostic> DiagnosticsById(string id) =>
        Diagnostics.Where(diagnostic => diagnostic.Id == id).ToArray();
}
