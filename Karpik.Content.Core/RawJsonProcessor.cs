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
        }
        catch (Exception ex)
        {
            diagnostics.Add(new ContentDiagnostic(ContentDiagnosticCodes.InvalidJsonContent, ContentDiagnosticSeverity.Error, relativePath, $"Source is not valid UTF-8: {ex.Message}"));
            return new ContentProcessorResult(Array.Empty<byte>(), Array.Empty<AssetId>(), diagnostics);
        }

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(text, new JsonDocumentOptions { AllowTrailingCommas = false, CommentHandling = JsonCommentHandling.Disallow });
        }
        catch (JsonException ex)
        {
            diagnostics.Add(new ContentDiagnostic(ContentDiagnosticCodes.InvalidJsonContent, ContentDiagnosticSeverity.Error, relativePath, $"Invalid JSON: {ex.Message}"));
            return new ContentProcessorResult(Array.Empty<byte>(), Array.Empty<AssetId>(), diagnostics);
        }

        using (doc)
        {
            // Canonicalize JSON for cooked artifact: fixed property order, invariant.
            string canonical = CanonicalJson.SerializeCanonical(doc.RootElement);
            byte[] cooked = Encoding.UTF8.GetBytes(canonical);

            // No automatic dependencies; explicit dependencies are validated at higher level.
            return new ContentProcessorResult(cooked, Array.Empty<AssetId>(), diagnostics);
        }
    }
}
