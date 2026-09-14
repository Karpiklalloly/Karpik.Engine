using System.Text.Json;

namespace Karpik.Content.Core;

public static class ContentMetaTemplate
{
    public static bool TryCreate(string relativePath, string assetNamespace, out string metaJson)
    {
        string? declaredType = Path.GetExtension(relativePath).ToLowerInvariant() switch
        {
            ".json" => AssetMeta.ExpectedDeclaredTypeRawJson,
            ".png" or ".jpg" or ".jpeg" => AssetMeta.ExpectedDeclaredTypeTexture,
            ".font-json" => AssetMeta.ExpectedDeclaredTypeFontJson,
            ".vert" or ".frag" => AssetMeta.ExpectedDeclaredTypeShader,
            _ => null
        };
        if (declaredType is null)
        {
            metaJson = string.Empty;
            return false;
        }

        string extension = Path.GetExtension(relativePath);
        string logicalPath = extension.Equals(".font-json", StringComparison.OrdinalIgnoreCase)
            ? relativePath[..^extension.Length] + ".font"
            : extension.Equals(".vert", StringComparison.OrdinalIgnoreCase) || extension.Equals(".frag", StringComparison.OrdinalIgnoreCase)
                ? relativePath
                : Path.ChangeExtension(relativePath, null)!;
        logicalPath = logicalPath
            .Replace(Path.DirectorySeparatorChar, '/')
            .Replace(Path.AltDirectorySeparatorChar, '/');
        string logicalName = $"{assetNamespace}/{logicalPath}";
        if (!AssetMeta.IsValidLogicalName(logicalName))
        {
            metaJson = string.Empty;
            return false;
        }

        metaJson = JsonSerializer.Serialize(new
        {
            schemaVersion = AssetMeta.CurrentSchemaVersion,
            assetId = AssetId.New().ToCanonicalString(),
            declaredType,
            logicalName,
            importSettings = new { },
            dependencies = Array.Empty<string>()
        }) + Environment.NewLine;
        return true;
    }
}
