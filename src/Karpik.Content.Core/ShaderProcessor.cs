using System.Text;

namespace Karpik.Content.Core;

public sealed class ShaderProcessor : IContentProcessor
{
    public string DeclaredType => AssetMeta.ExpectedDeclaredTypeShader;
    public string Version => "1.0.0";

    public ContentProcessorResult Process(ReadOnlySpan<byte> sourceBytes, AssetMeta meta, string relativePath, ContentProcessorContext context)
    {
        if (!IsSupportedExtension(relativePath))
        {
            return Failure(relativePath, "Shader sources must use .vert or .frag.");
        }
        if (sourceBytes.IsEmpty)
        {
            return Failure(relativePath, "Shader source must not be empty.");
        }

        try
        {
            _ = new UTF8Encoding(false, true).GetString(sourceBytes);
            return new ContentProcessorResult(sourceBytes.ToArray(), [], []);
        }
        catch (DecoderFallbackException exception)
        {
            return Failure(relativePath, $"Shader source is not valid UTF-8: {exception.Message}");
        }
    }

    private static bool IsSupportedExtension(string relativePath)
    {
        string extension = Path.GetExtension(relativePath);
        return extension.Equals(".vert", StringComparison.OrdinalIgnoreCase)
               || extension.Equals(".frag", StringComparison.OrdinalIgnoreCase);
    }

    private static ContentProcessorResult Failure(string relativePath, string message) =>
        new([], [], [new ContentDiagnostic(
            ContentDiagnosticCodes.InvalidShaderContent,
            ContentDiagnosticSeverity.Error,
            relativePath,
            message)]);
}
