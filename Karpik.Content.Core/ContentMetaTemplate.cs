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
            _ => null
        };
        if (declaredType is null)
        {
            metaJson = string.Empty;
            return false;
        }

        string logicalPath = Path.ChangeExtension(relativePath, null)!
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
