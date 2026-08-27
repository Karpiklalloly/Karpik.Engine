using Karpik.Content.Core;
using Xunit;

namespace Karpik.Content.Tests;

public sealed class AssetMetaTests
{
    [Fact]
    public void Parse_ValidMeta_Succeeds()
    {
        string guid = Guid.NewGuid().ToString("D").ToLowerInvariant();
        string json = $$"""{"schemaVersion":1,"assetId":"{{guid}}","declaredType":"raw-json","logicalName":"game/foo","importSettings":{},"dependencies":[]}""";
        var diagnostics = new List<ContentDiagnostic>();
        AssetMeta meta = AssetMeta.Parse(json, "foo.json.meta", diagnostics);
        Assert.Equal(guid, meta.AssetId.ToCanonicalString());
        Assert.Equal("raw-json", meta.DeclaredType);
        Assert.Equal("game/foo", meta.LogicalName);
        Assert.Empty(diagnostics);
    }

    [Fact]
    public void Parse_UpperCaseGuid_Normalized()
    {
        string guidUpper = Guid.NewGuid().ToString("D").ToUpperInvariant();
        string guidLower = guidUpper.ToLowerInvariant();
        string json = $$"""{"schemaVersion":1,"assetId":"{{guidUpper}}","declaredType":"raw-json","logicalName":"game/foo"}""";
        var diagnostics = new List<ContentDiagnostic>();
        AssetMeta meta = AssetMeta.Parse(json, "foo.json.meta", diagnostics);
        Assert.Equal(guidLower, meta.AssetId.ToCanonicalString());
    }

    [Fact]
    public void Parse_MissingAssetId_ReportsDiagnostic()
    {
        string json = """{"schemaVersion":1,"declaredType":"raw-json","logicalName":"game/foo"}""";
        var diagnostics = new List<ContentDiagnostic>();
        Assert.Throws<InvalidDataException>(() => AssetMeta.Parse(json, "foo.json.meta", diagnostics));
        Assert.Contains(diagnostics, d => d.Code == ContentDiagnosticCodes.MissingMetaField);
    }

    [Fact]
    public void Parse_InvalidGuid_ReportsDiagnostic()
    {
        string json = """{"schemaVersion":1,"assetId":"not-a-guid","declaredType":"raw-json","logicalName":"game/foo"}""";
        var diagnostics = new List<ContentDiagnostic>();
        Assert.Throws<InvalidDataException>(() => AssetMeta.Parse(json, "foo.json.meta", diagnostics));
        Assert.Contains(diagnostics, d => d.Code == ContentDiagnosticCodes.InvalidAssetId);
    }

    [Fact]
    public void Parse_InvalidLogicalName_ReportsDiagnostic()
    {
        string guid = Guid.NewGuid().ToString("D");
        string json = $$"""{"schemaVersion":1,"assetId":"{{guid}}","declaredType":"raw-json","logicalName":"invalid"}""";
        var diagnostics = new List<ContentDiagnostic>();
        Assert.Throws<InvalidDataException>(() => AssetMeta.Parse(json, "foo.json.meta", diagnostics));
        Assert.Contains(diagnostics, d => d.Code == ContentDiagnosticCodes.InvalidLogicalName);
    }

    [Fact]
    public void Parse_InvalidLogicalName_Traversal_ReportsDiagnostic()
    {
        string guid = Guid.NewGuid().ToString("D");
        string json = $$"""{"schemaVersion":1,"assetId":"{{guid}}","declaredType":"raw-json","logicalName":"game/../evil"}""";
        var diagnostics = new List<ContentDiagnostic>();
        Assert.Throws<InvalidDataException>(() => AssetMeta.Parse(json, "foo.json.meta", diagnostics));
        Assert.Contains(diagnostics, d => d.Code == ContentDiagnosticCodes.InvalidLogicalName);
    }

    [Fact]
    public void ToCanonicalMetaJson_IsDeterministicAndOrdered()
    {
        var guid = Guid.NewGuid();
        var id = new AssetId(guid);
        string json1 = $$"""{"schemaVersion":1,"assetId":"{{guid:D}}","declaredType":"raw-json","logicalName":"game/foo","importSettings":{"b":2,"a":1},"dependencies":["{{Guid.NewGuid():D}}"]}""";
        // Create two metas with same data but different importSettings order and dependencies order
        var diagnostics = new List<ContentDiagnostic>();
        AssetMeta meta1 = AssetMeta.Parse(json1, "a", diagnostics);
        // Now create another json with reversed order
        string deps = string.Join(",", meta1.Dependencies.Select(d => $"\"{d.ToCanonicalString()}\""));
        string json2 = $$"""{"logicalName":"game/foo","declaredType":"raw-json","assetId":"{{guid:D}}","schemaVersion":1,"importSettings":{"a":1,"b":2},"dependencies":[{{deps}}]}""";
        var diagnostics2 = new List<ContentDiagnostic>();
        AssetMeta meta2 = AssetMeta.Parse(json2, "a", diagnostics2);

        string canonical1 = meta1.ToCanonicalMetaJson();
        string canonical2 = meta2.ToCanonicalMetaJson();
        Assert.Equal(canonical1, canonical2);
    }

    [Fact]
    public void IsValidLogicalName_Validation()
    {
        Assert.True(AssetMeta.IsValidLogicalName("game/foo"));
        Assert.True(AssetMeta.IsValidLogicalName("game/foo/bar"));
        Assert.True(AssetMeta.IsValidLogicalName("my-namespace/path_123"));
        Assert.False(AssetMeta.IsValidLogicalName("game"));
        Assert.False(AssetMeta.IsValidLogicalName("game/"));
        Assert.False(AssetMeta.IsValidLogicalName("/game/foo"));
        Assert.False(AssetMeta.IsValidLogicalName("game//foo"));
        Assert.False(AssetMeta.IsValidLogicalName("game/../foo"));
        Assert.False(AssetMeta.IsValidLogicalName("game/foo\\bar"));
    }
}
