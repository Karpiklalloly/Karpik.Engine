using System.Text;
using System.Text.Json;

namespace Karpik.Content.Core;

public sealed class RawJsonProcessor : IContentProcessor
{
    public string DeclaredType => AssetMeta.ExpectedDeclaredTypeRawJson;
    public string Version => "1.0.0";

    public ContentProcessorResult Process(ReadOnlySpan<byte> sourceBytes, AssetMeta meta, string relativePath)
    {
        var diagnostics = new List<ContentDiagnostic>();

        // Validate JSON
        string text;
        try
        {
            text = Encoding.UTF8.GetString(sourceBytes);
            if (text.StartsWith('\uFEFF'))
            {
                text = text[1..];
            }
        }
        catch (Exception ex)
        {
            diagnostics.Add(new ContentDiagnostic(ContentDiagnosticCodes.InvalidJsonContent,
                ContentDiagnosticSeverity.Error, relativePath, $"Source is not valid UTF-8: {ex.Message}"));
            return new ContentProcessorResult([], [], diagnostics);
        }

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(text,
                new JsonDocumentOptions
                {
                    AllowTrailingCommas = false,
                    CommentHandling = JsonCommentHandling.Disallow
                });
        }
        catch (JsonException ex)
        {
            diagnostics.Add(new ContentDiagnostic(ContentDiagnosticCodes.InvalidJsonContent,
                ContentDiagnosticSeverity.Error, relativePath, $"Invalid JSON: {ex.Message}"));
            return new ContentProcessorResult([], [], diagnostics);
        }

        using (doc)
        {
            // No automatic dependencies; explicit dependencies are validated at higher level.
            return new ContentProcessorResult(Encoding.UTF8.GetBytes(CanonicalJson.SerializeCanonical(doc.RootElement)),
                [], diagnostics);
        }
    }
}
