using Karpik.Content.Core;
using Karpik.Content.Codegen;
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
}
