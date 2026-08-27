using Karpik.Content.Core;

namespace Karpik.Content.Tests;

internal sealed class TemporaryDirectory : IDisposable
{
    public TemporaryDirectory()
    {
        RootPath = Path.Combine(Path.GetTempPath(), "karpik-content-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(RootPath);
    }

    public string RootPath { get; }

    public string CreateSubdirectory(string name)
    {
        string path = Path.Combine(RootPath, name);
        Directory.CreateDirectory(path);
        return path;
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(RootPath))
            {
                Directory.Delete(RootPath, recursive: true);
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}

internal static class TestFixtures
{
    public static string CreateSourceFile(string sourceRoot, string relativePath, string content, AssetId? id = null, string? logicalName = null, string declaredType = "raw-json", object? importSettings = null, IEnumerable<AssetId>? dependencies = null)
    {
        string fullPath = Path.Combine(sourceRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
        string? dir = Path.GetDirectoryName(fullPath);
        if (dir is not null) Directory.CreateDirectory(dir);
        File.WriteAllText(fullPath, content);

        AssetId assetId = id ?? new AssetId(Guid.NewGuid());
        string logName = logicalName ?? $"game/{Path.GetFileNameWithoutExtension(relativePath)}";

        var meta = new
        {
            schemaVersion = 1,
            assetId = assetId.ToCanonicalString(),
            declaredType = declaredType,
            logicalName = logName,
            importSettings = importSettings ?? new { },
            dependencies = dependencies?.Select(d => d.ToCanonicalString()).ToArray() ?? Array.Empty<string>()
        };

        string metaJson = System.Text.Json.JsonSerializer.Serialize(meta, new System.Text.Json.JsonSerializerOptions { WriteIndented = false });
        // Write with pretty for easier debugging but parser should handle any whitespace
        File.WriteAllText(fullPath + ".meta", metaJson);
        return assetId.ToCanonicalString();
    }

    public static string CreateRawMeta(string assetId, string logicalName, string declaredType = "raw-json", object? importSettings = null, IEnumerable<string>? dependencies = null)
    {
        var meta = new
        {
            schemaVersion = 1,
            assetId = assetId,
            declaredType = declaredType,
            logicalName = logicalName,
            importSettings = importSettings ?? new { },
            dependencies = dependencies ?? Array.Empty<string>()
        };
        return System.Text.Json.JsonSerializer.Serialize(meta);
    }
}
