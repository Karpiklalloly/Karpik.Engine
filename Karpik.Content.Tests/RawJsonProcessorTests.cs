using Karpik.Content.Core;
using System.Text;
using Xunit;

namespace Karpik.Content.Tests;

public sealed class RawJsonProcessorTests
{
    private readonly RawJsonProcessor _processor = new();

    [Fact]
    public void Process_ValidJson_CookedIsCanonical()
    {
        string guid = Guid.NewGuid().ToString("D");
        var meta = CreateMeta(guid);
        string json1 = """{"b":2,"a":1}""";
        string json2 = """{"a":1,"b":2}""";

        var result1 = _processor.Process(Encoding.UTF8.GetBytes(json1), meta, "a.json");
        var result2 = _processor.Process(Encoding.UTF8.GetBytes(json2), meta, "a.json");

        Assert.Empty(result1.Diagnostics.Where(d => d.Severity == ContentDiagnosticSeverity.Error));
        Assert.Empty(result2.Diagnostics.Where(d => d.Severity == ContentDiagnosticSeverity.Error));
        Assert.Equal(Encoding.UTF8.GetString(result1.CookedBytes), Encoding.UTF8.GetString(result2.CookedBytes));
        Assert.Equal("""{"a":1,"b":2}""", Encoding.UTF8.GetString(result1.CookedBytes));
    }

    [Fact]
    public void Process_Utf8BomJson_CookedIsCanonical()
    {
        byte[] source = [0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes("""{"a":1}""")];

        ContentProcessorResult result = _processor.Process(source, CreateMeta(Guid.NewGuid().ToString("D")), "bom.json");

        Assert.DoesNotContain(result.Diagnostics, d => d.Severity == ContentDiagnosticSeverity.Error);
        Assert.Equal("""{"a":1}""", Encoding.UTF8.GetString(result.CookedBytes));
    }

    [Fact]
    public void Process_InvalidJson_ReportsError()
    {
        string guid = Guid.NewGuid().ToString("D");
        var meta = CreateMeta(guid);
        string invalid = """{"a": }""";
        var result = _processor.Process(Encoding.UTF8.GetBytes(invalid), meta, "bad.json");
        Assert.Contains(result.Diagnostics, d => d.Code == ContentDiagnosticCodes.InvalidJsonContent && d.Severity == ContentDiagnosticSeverity.Error);
        Assert.Empty(result.CookedBytes);
    }

    [Fact]
    public void HashChanges_AfterSourceChange()
    {
        string guid = Guid.NewGuid().ToString("D");
        var meta = CreateMeta(guid);
        string json1 = """{"a":1}""";
        string json2 = """{"a":2}""";
        string canonicalMeta = meta.ToCanonicalMetaJson();
        string hash1 = ContentHashing.ComputeArtifactHash(Encoding.UTF8.GetBytes(json1), canonicalMeta, _processor.Version);
        string hash2 = ContentHashing.ComputeArtifactHash(Encoding.UTF8.GetBytes(json2), canonicalMeta, _processor.Version);
        Assert.NotEqual(hash1, hash2);
    }

    [Fact]
    public void HashChanges_AfterMetaChange()
    {
        var guid = Guid.NewGuid().ToString("D");
        var meta1 = CreateMeta(guid, logicalName: "game/a");
        var meta2 = CreateMeta(guid, logicalName: "game/b");
        string json = """{"a":1}""";
        string hash1 = ContentHashing.ComputeArtifactHash(Encoding.UTF8.GetBytes(json), meta1.ToCanonicalMetaJson(), _processor.Version);
        string hash2 = ContentHashing.ComputeArtifactHash(Encoding.UTF8.GetBytes(json), meta2.ToCanonicalMetaJson(), _processor.Version);
        Assert.NotEqual(hash1, hash2);
    }

    [Fact]
    public void HashChanges_AfterProcessorVersionChange()
    {
        string guid = Guid.NewGuid().ToString("D");
        var meta = CreateMeta(guid);
        string json = """{"a":1}""";
        string canonicalMeta = meta.ToCanonicalMetaJson();
        string hash1 = ContentHashing.ComputeArtifactHash(Encoding.UTF8.GetBytes(json), canonicalMeta, "1.0.0");
        string hash2 = ContentHashing.ComputeArtifactHash(Encoding.UTF8.GetBytes(json), canonicalMeta, "2.0.0");
        Assert.NotEqual(hash1, hash2);
    }

    private static AssetMeta CreateMeta(string guid, string logicalName = "game/foo")
    {
        string json = "{\"schemaVersion\":1,\"assetId\":\"" + guid + "\",\"declaredType\":\"raw-json\",\"logicalName\":\"" + logicalName + "\",\"importSettings\":{}}";
        var diagnostics = new List<ContentDiagnostic>();
        return AssetMeta.Parse(json, "test.meta", diagnostics);
    }
}
