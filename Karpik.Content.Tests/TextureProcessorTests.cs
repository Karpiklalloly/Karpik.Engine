using Karpik.Content.Core;
using Xunit;

namespace Karpik.Content.Tests;

public sealed class TextureProcessorTests
{
    private static readonly byte[] ValidPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVQIHWP4z8DwHwAFgAI/ScL4uQAAAABJRU5ErkJggg==");

    private static readonly byte[] ValidJpeg = Convert.FromBase64String(
        "/9j/4AAQSkZJRgABAQEAYABgAAD/2wBDAAMCAgMCAgMDAwMEAwMEBQgFBQQEBQoHBwYIDAoMDAsKCwsNDhIQDQ4RDgsLEBYQERMUFRUVDA8XGBYUGBIUFRT/2wBDAQMEBAUEBQkFBQkUDQsNFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBT/wAARCAABAAEDASIAAhEBAxEB/8QAHwAAAQUBAQEBAQEAAAAAAAAAAAECAwQFBgcICQoL/8QAtRAAAgEDAwIEAwUFBAQAAAF9AQIDAAQRBRIhMUEGE1FhByJxFDKBkaEII0KxwRVS0fAkM2JyggkKFhcYGRolJicoKSo0NTY3ODk6Q0RFRkdISUpTVFVWV1hZWmNkZWZnaGlqc3R1dnd4eXqDhIWGh4iJipKTlJWWl5iZmqKjpKWmp6ipqrKztLW2t7i5usLDxMXGx8jJytLT1NXW19jZ2uHi4+Tl5ufo6erx8vP09fb3+Pn6/8QAHwEAAwEBAQEBAQEBAQAAAAAAAAECAwQFBgcICQoL/8QAtREAAgECBAQDBAcFBAQAAQJ3AAECAxEEBSExBhJBUQdhcRMiMoEIFEKRobHBCSMzUvAVYnLRChYkNOEl8RcYGRomJygpKjU2Nzg5OkNERUZHSElKU1RVVldYWVpjZGVmZ2hpanN0dXZ3eHl6goOEhYaHiImKkpOUlZaXmJmaoqOkpaanqKmqsrO0tba3uLm6wsPExcbHyMnK0tPU1dbX2Nna4uPk5ebn6Onq8vP09fb3+Pn6/9oADAMBAAIRAxEAPwD50ooor8MP9Uz/2Q==");

    [Theory]
    [InlineData("pixel.png")]
    [InlineData("pixel.jpg")]
    [InlineData("pixel.jpeg")]
    public void Process_ValidImage_PreservesEncodedBytes(string relativePath)
    {
        byte[] source = relativePath.EndsWith("png", StringComparison.Ordinal) ? ValidPng : ValidJpeg;
        var processor = new TextureProcessor();

        ContentProcessorResult result = processor.Process(source, CreateMeta(), relativePath);

        Assert.DoesNotContain(result.Diagnostics, d => d.Severity == ContentDiagnosticSeverity.Error);
        Assert.Equal(source, result.CookedBytes);
        Assert.NotSame(source, result.CookedBytes);
        Assert.Empty(result.Dependencies);
    }

    [Fact]
    public void Process_MalformedImage_ReportsError()
    {
        var processor = new TextureProcessor();

        ContentProcessorResult result = processor.Process([1, 2, 3], CreateMeta(), "broken.png");

        Assert.Contains(result.Diagnostics, d => d.Severity == ContentDiagnosticSeverity.Error);
        Assert.Empty(result.CookedBytes);
    }

    private static AssetMeta CreateMeta()
    {
        string json = "{\"schemaVersion\":1,\"assetId\":\"" + Guid.NewGuid().ToString("D")
                      + "\",\"declaredType\":\"texture\",\"logicalName\":\"game/pixel\",\"importSettings\":{}}";
        return AssetMeta.Parse(json, "pixel.meta", []);
    }
}
