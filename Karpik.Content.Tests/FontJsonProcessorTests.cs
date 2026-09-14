using Karpik.Content.Core;
using Xunit;

namespace Karpik.Content.Tests;

public sealed class FontJsonProcessorTests
{
    [Fact]
    public void Process_ValidFontJson_PreservesSourceBytes()
    {
        byte[] source = "{\"atlas\":{\"type\":\"msdf\"},\"glyphs\":[]}"u8.ToArray();

        ContentProcessorResult result = new FontJsonProcessor().Process(source, CreateMeta(), "default.font-json", default);

        Assert.DoesNotContain(result.Diagnostics, d => d.Severity == ContentDiagnosticSeverity.Error);
        Assert.Equal(source, result.CookedBytes);
        Assert.NotSame(source, result.CookedBytes);
        Assert.Empty(result.Dependencies);
    }

    [Fact]
    public void Process_Utf8BomFontJson_PreservesSourceBytes()
    {
        byte[] source = [0xEF, 0xBB, 0xBF, .. "{}"u8.ToArray()];

        ContentProcessorResult result = new FontJsonProcessor().Process(source, CreateMeta(), "default.font-json", default);

        Assert.DoesNotContain(result.Diagnostics, d => d.Severity == ContentDiagnosticSeverity.Error);
        Assert.Equal(source, result.CookedBytes);
    }

    [Fact]
    public void Process_InvalidFontJson_ReportsJsonError()
    {
        ContentProcessorResult result = new FontJsonProcessor().Process("not-json"u8, CreateMeta(), "default.font-json", default);

        Assert.Contains(result.Diagnostics, d => d.Code == ContentDiagnosticCodes.InvalidJsonContent);
        Assert.Empty(result.CookedBytes);
    }

    private static AssetMeta CreateMeta()
    {
        string json = "{\"schemaVersion\":1,\"assetId\":\"" + Guid.NewGuid().ToString("D")
                      + "\",\"declaredType\":\"font-json\",\"logicalName\":\"game/default\",\"importSettings\":{}}";
        return AssetMeta.Parse(json, "default.font-json.meta", []);
    }
}
