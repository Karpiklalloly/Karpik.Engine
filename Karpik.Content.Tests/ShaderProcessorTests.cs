using Karpik.Content.Core;
using Xunit;

namespace Karpik.Content.Tests;

public sealed class ShaderProcessorTests
{
    [Theory]
    [InlineData("main.vert")]
    [InlineData("main.frag")]
    public void Process_ValidShader_PreservesSourceBytes(string relativePath)
    {
        byte[] source = "#version 450\nvoid main() {}"u8.ToArray();

        ContentProcessorResult result = new ShaderProcessor().Process(source, CreateMeta(), relativePath, default);

        Assert.DoesNotContain(result.Diagnostics, d => d.Severity == ContentDiagnosticSeverity.Error);
        Assert.Equal(source, result.CookedBytes);
        Assert.NotSame(source, result.CookedBytes);
        Assert.Empty(result.Dependencies);
    }

    [Fact]
    public void Process_EmptyShader_ReportsError()
    {
        ContentProcessorResult result = new ShaderProcessor().Process([], CreateMeta(), "main.frag", default);

        Assert.Contains(result.Diagnostics, d => d.Code == ContentDiagnosticCodes.InvalidShaderContent);
        Assert.Empty(result.CookedBytes);
    }

    [Fact]
    public void Process_InvalidUtf8Shader_ReportsError()
    {
        ContentProcessorResult result = new ShaderProcessor().Process([0xff], CreateMeta(), "main.frag", default);

        Assert.Contains(result.Diagnostics, d => d.Code == ContentDiagnosticCodes.InvalidShaderContent);
        Assert.Empty(result.CookedBytes);
    }

    private static AssetMeta CreateMeta()
    {
        string json = "{\"schemaVersion\":1,\"assetId\":\"" + Guid.NewGuid().ToString("D")
                      + "\",\"declaredType\":\"shader\",\"logicalName\":\"game/main\",\"importSettings\":{}}";
        return AssetMeta.Parse(json, "main.frag.meta", []);
    }
}
