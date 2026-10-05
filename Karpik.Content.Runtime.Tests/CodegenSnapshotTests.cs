using System.Collections.Immutable;
using System.Reflection;
using System.Text;
using Karpik.Content.Codegen;
using Karpik.Content.Core;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;
using Xunit;

namespace Karpik.Content.Runtime.Tests;

public sealed class CodegenSnapshotTests
{
    private static ContentManifestEntry MakeEntry(string guid, string logical, string declaredType = "raw-json")
    {
        return new ContentManifestEntry(
            AssetId.Parse(guid),
            declaredType,
            logical,
            "hash-import",
            "hash-source",
            $"artifacts/{guid.Substring(0,2)}/{guid.Substring(2,2)}/hash.cooked",
            2,
            Array.Empty<AssetId>());
    }

    [Fact]
    public void Snapshot_GeneratesSorted()
    {
        var entryA = MakeEntry("16755701-7b8a-4dfe-ad91-b5a28ba28882", "game/a");
        var entryB = MakeEntry("125bb6cd-7b8a-4dfe-ad91-b5a28ba28882", "game/b");
        string manifestJson = new ContentManifest(1, new[] { entryA, entryB }).ToCanonicalJson();
        string generated = ContentCodegenGenerator.GenerateForTest(manifestJson, new Dictionary<string, string> { { "raw-json", "RawJsonPayload" } });
        Assert.Contains("Game_A", generated);
        Assert.Contains("Game_B", generated);
        // sorted by AssetId guid compare: 125bb6cd < 16755701, so Game_B should appear before Game_A
        Assert.True(generated.IndexOf("125bb6cd", StringComparison.Ordinal) < generated.IndexOf("16755701", StringComparison.Ordinal));
        Assert.True(generated.IndexOf("Game_B", StringComparison.Ordinal) < generated.IndexOf("Game_A", StringComparison.Ordinal));
    }

    [Fact]
    public void PascalCase_Collision_AppendsSuffix()
    {
        // game/a-b and game/a_b both map to same PascalCase if hyphen/underscore normalized => collision => _2
        var entry1 = MakeEntry("11111111-1111-1111-1111-111111111111", "game/a-b");
        var entry2 = MakeEntry("22222222-2222-2222-2222-222222222222", "game/a_b");
        string manifestJson = new ContentManifest(1, new[] { entry1, entry2 }).ToCanonicalJson();
        string generated = ContentCodegenGenerator.GenerateForTest(manifestJson, new Dictionary<string, string> { { "raw-json", "RawJsonPayload" } });
        // first should be Game_A_B, second should be Game_A_B_2
        Assert.Contains("Game_A_B", generated);
        Assert.Contains("Game_A_B_2", generated);
    }

    [Fact]
    public void AllAndByPath_Generated_Sorted()
    {
        var entryA = MakeEntry("33333333-3333-3333-3333-333333333333", "game/zebra");
        var entryB = MakeEntry("11111111-1111-1111-1111-111111111111", "game/apple");
        string manifestJson = new ContentManifest(1, new[] { entryA, entryB }).ToCanonicalJson();
        string generated = ContentCodegenGenerator.GenerateForTest(manifestJson, new Dictionary<string, string> { { "raw-json", "RawJsonPayload" } });
        Assert.Contains("All", generated);
        Assert.Contains("ByPath", generated);
        Assert.Contains("Game_Apple_Path", generated);
        Assert.Contains("Game_Zebra_Path", generated);
        // All should be sorted by AssetId: apple entry (111...) before zebra (333...)
        var allIndexApple = generated.IndexOf("11111111", StringComparison.Ordinal);
        var allIndexZebra = generated.IndexOf("33333333", StringComparison.Ordinal);
        Assert.True(allIndexApple < allIndexZebra);
    }

    [Fact]
    public void DeclaredType_MappedToClrType()
    {
        var entry = MakeEntry("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa", "game/hero", "my-hero");
        string manifestJson = new ContentManifest(1, new[] { entry }).ToCanonicalJson();
        var map = new Dictionary<string, string> { { "raw-json", "RawJsonPayload" }, { "my-hero", "HeroConfig" } };
        string generated = ContentCodegenGenerator.GenerateForTest(manifestJson, map);
        Assert.Contains("HeroConfig", generated);
        Assert.Contains("Game_Hero", generated);
    }

    [Fact]
    public void UnmappedDeclaredType_FallbackToRawJsonPayload()
    {
        var entry = MakeEntry("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb", "game/unknown", "unknown-type");
        string manifestJson = new ContentManifest(1, new[] { entry }).ToCanonicalJson();
        var map = new Dictionary<string, string> { { "raw-json", "RawJsonPayload" } };
        string generated = ContentCodegenGenerator.GenerateForTest(manifestJson, map);
        Assert.Contains("RawJsonPayload", generated);
        Assert.Contains("Game_Unknown", generated);
    }

    [Fact]
    public void GeneratesDeterministic_SameManifest_ByteIdentical()
    {
        var entry = MakeEntry("cccccccc-cccc-cccc-cccc-cccccccccccc", "game/deterministic");
        var manifest = new ContentManifest(1, new[] { entry });
        string json1 = manifest.ToCanonicalJson();
        string json2 = manifest.ToCanonicalJson();
        var map = new Dictionary<string, string> { { "raw-json", "RawJsonPayload" } };
        string gen1 = ContentCodegenGenerator.GenerateForTest(json1, map);
        string gen2 = ContentCodegenGenerator.GenerateForTest(json2, map);
        Assert.Equal(gen1, gen2);
    }

    // === Driver-based tests for incremental hash, diagnostics KCO301/KCO304, IsRoslynComponent ===

    [Fact]
    public void Driver_Deterministic_SameManifest_ProducesIdenticalOutput()
    {
        var entryA = MakeEntry("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa", "game/hero", "raw-json");
        var entryB = MakeEntry("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb", "game/level", "raw-json");
        string manifestJson = new ContentManifest(1, new[] { entryB, entryA }).ToCanonicalJson(); // intentionally unsorted

        string tempPath = Path.Combine(Path.GetTempPath(), $"karpik_manifest_{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(tempPath, manifestJson, new UTF8Encoding(false));

            // Minimal compilation with ContentType reference so generator can resolve typeMap,
            // but we will use raw-json fallback which does not require specific ContentType.
            const string source = """
                using Karpik.Content.Runtime;
                namespace TestGame { [ContentType("raw-json")] public sealed class RawJsonPayload { } }
                """;

            var result1 = RunDriver(source, tempPath, Array.Empty<AdditionalText>());
            var result2 = RunDriver(source, tempPath, Array.Empty<AdditionalText>());

            Assert.True(result1.GeneratedSources.TryGetValue("ContentRefs.g.cs", out var gen1), "Expected ContentRefs.g.cs generation");
            Assert.True(result2.GeneratedSources.TryGetValue("ContentRefs.g.cs", out var gen2), "Expected ContentRefs.g.cs generation on second run");
            Assert.Equal(gen1, gen2);

            // Also verify that diagnostics are deterministic (no random KCO warnings beyond expected)
            var diagIds1 = result1.Diagnostics.Select(d => d.Id).OrderBy(id => id).ToArray();
            var diagIds2 = result2.Diagnostics.Select(d => d.Id).OrderBy(id => id).ToArray();
            Assert.Equal(diagIds1, diagIds2);

            // Ensure sorted order regardless of manifest input order (hash deterministic)
            Assert.True(gen1.IndexOf("aaaaaaaa", StringComparison.Ordinal) < gen1.IndexOf("bbbbbbbb", StringComparison.Ordinal));
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
    }

    [Fact]
    public void Driver_MissingManifest_Reports_KCO301_And_FallsBackToAdditionalTexts()
    {
        // Manifest path points to non-existent file; generator should report KCO301 and fallback to .json.meta AdditionalTexts
        string missingPath = Path.Combine(Path.GetTempPath(), $"karpik_missing_{Guid.NewGuid():N}.json");
        string metaJson = """{"assetId":"11111111-1111-1111-1111-111111111111","declaredType":"raw-json","logicalName":"game/fallback"}""";
        var additionalTexts = new AdditionalText[]
        {
            new TestAdditionalText("Assets/game_fallback.json.meta", metaJson),
            new TestAdditionalText("Assets/ignore.txt", "should be ignored"),
        };

        const string source = """
            using Karpik.Content.Runtime;
            namespace TestGame { [ContentType("raw-json")] public sealed class RawJsonPayload { } }
            """;

        var result = RunDriver(source, missingPath, additionalTexts);

        // Should report KCO301 for missing manifest
        Assert.Contains(result.Diagnostics, d => d.Id == "KCO301");
        Assert.True(result.GeneratedSources.TryGetValue("ContentRefs.g.cs", out var generated), "Should fallback to AdditionalTexts and still generate");
        Assert.Contains("Game_Fallback", generated);
        Assert.Contains("11111111", generated);
    }

    [Fact]
    public void Driver_UnmappedDeclaredType_Reports_KCO304()
    {
        var entry = MakeEntry("dddddddd-dddd-dddd-dddd-dddddddddddd", "game/unknown", "unknown-type");
        string manifestJson = new ContentManifest(1, new[] { entry }).ToCanonicalJson();
        string tempPath = Path.Combine(Path.GetTempPath(), $"karpik_manifest_{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(tempPath, manifestJson, new UTF8Encoding(false));

            // Provide source with only raw-json ContentType, not unknown-type, so KCO304 should fire
            const string source = """
                using Karpik.Content.Runtime;
                namespace TestGame { [ContentType("raw-json")] public sealed class RawJsonPayload { } }
                """;

            var result = RunDriver(source, tempPath, Array.Empty<AdditionalText>());

            Assert.Contains(result.Diagnostics, d => d.Id == "KCO304");
            Assert.True(result.GeneratedSources.TryGetValue("ContentRefs.g.cs", out var generated));
            Assert.Contains("RawJsonPayload", generated);
            Assert.Contains("Game_Unknown", generated);
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
    }

    [Fact]
    public void GenerateForTestWithDiagnostics_UnmappedDeclaredType_Reports_KCO304()
    {
        var entry = MakeEntry("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee", "game/unknown2", "unknown-type-2");
        string manifestJson = new ContentManifest(1, new[] { entry }).ToCanonicalJson();
        var map = new Dictionary<string, string> { { "raw-json", "RawJsonPayload" } };
        var diagnostics = new List<Diagnostic>();
        string generated = ContentCodegenGenerator.GenerateForTestWithDiagnostics(manifestJson, map, diagnostics);
        Assert.Contains("RawJsonPayload", generated);
        Assert.Contains(diagnostics, d => d.Id == "KCO304");
    }

    [Fact]
    public void Generator_IsIncremental_And_IsRoslynComponent()
    {
        // Verify generator is correctly attributed for incremental generation
        var genType = typeof(ContentCodegenGenerator);
        var genAttr = genType.GetCustomAttribute<GeneratorAttribute>();
        Assert.NotNull(genAttr);
        Assert.True(typeof(IIncrementalGenerator).IsAssignableFrom(genType), "ContentCodegenGenerator must implement IIncrementalGenerator");

        // Verify csproj has IsRoslynComponent=true and no suppressed NU1903 / RS1035 etc
        string repoRoot = FindRepositoryRoot();
        string csprojPath = Path.Combine(repoRoot, "Karpik.Content.Codegen", "Karpik.Content.Codegen.csproj");
        Assert.True(File.Exists(csprojPath), $"csproj not found at {csprojPath}");
        string csproj = File.ReadAllText(csprojPath);
        Assert.Contains("<IsRoslynComponent>true</IsRoslynComponent>", csproj);
        Assert.DoesNotContain("NU1903", csproj);
        Assert.DoesNotContain("<NoWarn>", csproj);
    }

    [Fact]
    public void Driver_DuplicateLogical_Reports_KCO302_And_InvalidLogical_Reports_KCO303()
    {
        // Test GenerateForTestWithDiagnostics for duplicate and invalid logicalName paths
        var entry1 = MakeEntry("11111111-1111-1111-1111-111111111111", "game/a-b");
        var entry2 = MakeEntry("22222222-2222-2222-2222-222222222222", "game/a_b"); // same field name -> KCO302
        string manifestJson = new ContentManifest(1, new[] { entry1, entry2 }).ToCanonicalJson();
        var map = new Dictionary<string, string> { { "raw-json", "RawJsonPayload" } };
        var diagnostics = new List<Diagnostic>();
        string generated = ContentCodegenGenerator.GenerateForTestWithDiagnostics(manifestJson, map, diagnostics);
        Assert.Contains(diagnostics, d => d.Id == "KCO302");
        Assert.Contains("Game_A_B_2", generated);

        // Invalid logicalName (no slash) -> KCO303
        var invalidEntry = MakeEntry("33333333-3333-3333-3333-333333333333", "invalidlogical", "raw-json");
        string invalidJson = new ContentManifest(1, new[] { invalidEntry }).ToCanonicalJson();
        var invalidDiags = new List<Diagnostic>();
        string invalidGen = ContentCodegenGenerator.GenerateForTestWithDiagnostics(invalidJson, map, invalidDiags);
        Assert.Contains(invalidDiags, d => d.Id == "KCO303");
        Assert.Contains("33333333", invalidGen);
    }

    // --- Harness helpers ---

    private static DriverResult RunDriver(string source, string? manifestPath, IReadOnlyList<AdditionalText> additionalTexts)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source, CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Latest));
        var references = GetReferences();
        var compilation = CSharpCompilation.Create(
            "TestContentAssembly",
            new[] { syntaxTree },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true));

        var options = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!string.IsNullOrEmpty(manifestPath))
        {
            options["build_property.KarpikContentManifest"] = manifestPath;
        }

        var optionsProvider = new TestAnalyzerConfigOptionsProvider(options);
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            new IIncrementalGenerator[] { new ContentCodegenGenerator() }.Select(g => g.AsSourceGenerator()),
            parseOptions: (CSharpParseOptions)syntaxTree.Options,
            optionsProvider: optionsProvider,
            additionalTexts: additionalTexts.ToImmutableArray());

        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var updatedCompilation, out var driverDiagnostics);

        var runResult = driver.GetRunResult();
        var generated = runResult.Results
            .SelectMany(r => r.GeneratedSources)
            .ToDictionary(s => s.HintName, s => s.SourceText.ToString(), StringComparer.Ordinal);

        // Collect diagnostics from multiple sources: RunResult.Diagnostics (includes generator KCO*), driverDiagnostics, updatedCompilation
        var generatorDiagnostics = runResult.Results.SelectMany(r => r.Diagnostics);
        var allDiagnostics = runResult.Diagnostics
            .Concat(generatorDiagnostics)
            .Concat(driverDiagnostics)
            .Concat(updatedCompilation.GetDiagnostics())
            .Where(d => d.Severity >= DiagnosticSeverity.Warning)
            .ToImmutableArray();

        return new DriverResult(generated, allDiagnostics, updatedCompilation);
    }

    private static ImmutableArray<MetadataReference> GetReferences()
    {
        var tpas = (string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES");
        var framework = tpas!
            .Split(Path.PathSeparator)
            .Where(path => path.StartsWith(System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory(), StringComparison.OrdinalIgnoreCase))
            .Select(path => (MetadataReference)MetadataReference.CreateFromFile(path))
            .ToList();

        // Add project-specific references needed for ContentTypeAttribute resolution
        framework.Add(MetadataReference.CreateFromFile(typeof(ContentManifest).Assembly.Location));
        framework.Add(MetadataReference.CreateFromFile(typeof(ContentTypeAttribute).Assembly.Location));
        framework.Add(MetadataReference.CreateFromFile(typeof(AssetId).Assembly.Location));
        // DCFApixels not needed for content, but harmless

        return framework.ToImmutableArray();
    }

    private static string FindRepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Karpik.Content.Codegen", "Karpik.Content.Codegen.csproj")))
                return dir.FullName;
            if (File.Exists(Path.Combine(dir.FullName, "Directory.Build.props")))
                return dir.FullName;
            dir = dir.Parent;
        }
        // Fallback to current directory traversal
        dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Karpik.Content.Codegen", "Karpik.Content.Codegen.csproj")))
                return dir.FullName;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("Could not locate repository root");
    }

    private sealed class TestAdditionalText : AdditionalText
    {
        private readonly string _text;
        public TestAdditionalText(string path, string text)
        {
            Path = path;
            _text = text;
        }
        public override string Path { get; }
        public override SourceText? GetText(CancellationToken cancellationToken) => SourceText.From(_text, Encoding.UTF8);
    }

    private sealed class TestAnalyzerConfigOptionsProvider : AnalyzerConfigOptionsProvider
    {
        private readonly AnalyzerConfigOptions _global;
        public TestAnalyzerConfigOptionsProvider(IReadOnlyDictionary<string, string> values) => _global = new TestAnalyzerConfigOptions(values);
        public override AnalyzerConfigOptions GlobalOptions => _global;
        public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => EmptyAnalyzerConfigOptions.Instance;
        public override AnalyzerConfigOptions GetOptions(AdditionalText text) => EmptyAnalyzerConfigOptions.Instance;
    }

    private sealed class TestAnalyzerConfigOptions : AnalyzerConfigOptions
    {
        private readonly IReadOnlyDictionary<string, string> _values;
        public TestAnalyzerConfigOptions(IReadOnlyDictionary<string, string> values) => _values = values;
        public override bool TryGetValue(string key, out string value) => _values.TryGetValue(key, out value!);
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

    private sealed record DriverResult(IReadOnlyDictionary<string, string> GeneratedSources, ImmutableArray<Diagnostic> Diagnostics, Compilation Compilation);
}
