using StbImageSharp;

namespace Karpik.Content.Core;

public sealed class TextureProcessor : IContentProcessor
{
    public string DeclaredType => AssetMeta.ExpectedDeclaredTypeTexture;
    public string Version => "1.0.0";

    public ContentProcessorResult Process(ReadOnlySpan<byte> sourceBytes, AssetMeta meta, string relativePath, ContentProcessorContext context)
    {
        if (!IsSupportedExtension(relativePath))
        {
            return Failure(relativePath, "Texture sources must use .png, .jpg, or .jpeg.");
        }

        try
        {
            using var stream = new MemoryStream(sourceBytes.ToArray(), writable: false);
            ImageResult image = ImageResult.FromStream(stream, ColorComponents.RedGreenBlueAlpha);
            if (image.Width <= 0 || image.Height <= 0)
            {
                return Failure(relativePath, "Image dimensions must be positive.");
            }
        }
        catch (Exception exception) when (exception is InvalidDataException or ArgumentException or InvalidOperationException or NotSupportedException)
        {
            return Failure(relativePath, $"Invalid image: {exception.Message}");
        }

        return new ContentProcessorResult(sourceBytes.ToArray(), [], []);
    }

    private static bool IsSupportedExtension(string relativePath)
    {
        string extension = Path.GetExtension(relativePath);
        return extension.Equals(".png", StringComparison.OrdinalIgnoreCase)
               || extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase)
               || extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase);
    }

    private static ContentProcessorResult Failure(string relativePath, string message) =>
        new([], [], [new ContentDiagnostic(
            ContentDiagnosticCodes.InvalidImageContent,
            ContentDiagnosticSeverity.Error,
            relativePath,
            message)]);
}
