using System.Text.Json;

namespace Karpik.Content.Core;

public sealed class FontJsonProcessor : IContentProcessor
{
    public string DeclaredType => AssetMeta.ExpectedDeclaredTypeFontJson;
    public string Version => "1.0.0";

    public ContentProcessorResult Process(ReadOnlySpan<byte> sourceBytes, AssetMeta meta, string relativePath)
    {
        byte[] cookedBytes = sourceBytes.ToArray();
        try
        {
            using JsonDocument _ = JsonDocument.Parse(cookedBytes);
            return new ContentProcessorResult(cookedBytes, [], []);
        }
        catch (JsonException exception)
        {
            return new ContentProcessorResult([], [], [new ContentDiagnostic(
                ContentDiagnosticCodes.InvalidJsonContent,
                ContentDiagnosticSeverity.Error,
                relativePath,
                $"Invalid font JSON: {exception.Message}")]);
        }
    }
}
