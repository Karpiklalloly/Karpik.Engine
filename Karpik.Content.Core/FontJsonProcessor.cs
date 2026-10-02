using System.Text;
using System.Text.Json;

namespace Karpik.Content.Core;

public sealed class FontJsonProcessor : IContentProcessor
{
    public string DeclaredType => AssetMeta.ExpectedDeclaredTypeFontJson;
    public string Version => "1.0.0";

    public ContentProcessorResult Process(ReadOnlySpan<byte> sourceBytes, AssetMeta meta, string relativePath, ContentProcessorContext context)
    {
        byte[] cookedBytes = sourceBytes.ToArray();
        byte[] validationBytes = cookedBytes.AsSpan().StartsWith(Encoding.UTF8.Preamble)
            ? cookedBytes[Encoding.UTF8.Preamble.Length..]
            : cookedBytes;
        try
        {
            using JsonDocument _ = JsonDocument.Parse(validationBytes);
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
