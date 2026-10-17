using Karpik.Content.Core;
using Xunit;

namespace Karpik.Content.Tests;

public sealed class CanonicalJsonTests
{
    [Fact]
    public void SerializeCanonical_OrderIsDeterministic()
    {
        string json1 = """{"b":2,"a":1,"c":{"z":3,"y":2}}""";
        string json2 = """{"a":1,"c":{"y":2,"z":3},"b":2}""";
        using var doc1 = System.Text.Json.JsonDocument.Parse(json1);
        using var doc2 = System.Text.Json.JsonDocument.Parse(json2);
        string canon1 = CanonicalJson.SerializeCanonical(doc1.RootElement);
        string canon2 = CanonicalJson.SerializeCanonical(doc2.RootElement);
        Assert.Equal(canon1, canon2);
        Assert.Equal("""{"a":1,"b":2,"c":{"y":2,"z":3}}""", canon1);
    }

    [Fact]
    public void SerializeManifestCanonical_OrderByAssetId()
    {
        var id1 = new AssetId(Guid.Parse("22222222-2222-2222-2222-222222222222"));
        var id2 = new AssetId(Guid.Parse("11111111-1111-1111-1111-111111111111"));

        var entries = new[]
        {
            new ContentManifestEntry(id1, "raw-json", "game/b", "hash1", "hash2", "loc1", 10, Array.Empty<AssetId>()),
            new ContentManifestEntry(id2, "raw-json", "game/a", "hash1", "hash2", "loc2", 10, Array.Empty<AssetId>())
        };

        var manifest1 = new ContentManifest(1, entries);
        var manifest2 = new ContentManifest(1, entries.Reverse().ToArray());

        string json1 = manifest1.ToCanonicalJson();
        string json2 = manifest2.ToCanonicalJson();

        Assert.Equal(json1, json2);

        // Ensure order is by assetId ascending (111... before 222...)
        Assert.True(json1.IndexOf(id2.ToCanonicalString(), StringComparison.Ordinal) < json1.IndexOf(id1.ToCanonicalString(), StringComparison.Ordinal));
    }

    [Fact]
    public void ManifestRoundTrip_IsIdentical()
    {
        var id = new AssetId(Guid.NewGuid());
        var entry = new ContentManifestEntry(id, "raw-json", "game/foo", "importHash", "sourceHash", "artifacts/ab/cd/hash.cooked", 123, new[] { id });
        var manifest = new ContentManifest(1, new[] { entry });
        string json = manifest.ToCanonicalJson();
        var parsed = ContentManifest.Parse(json);
        Assert.Equal(manifest.ToCanonicalJson(), parsed.ToCanonicalJson());
    }
}
