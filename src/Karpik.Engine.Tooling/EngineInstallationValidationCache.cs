using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Karpik.Engine.Tooling;

/// <summary>Persists a metadata receipt for a payload that has already passed full validation.</summary>
public sealed class EngineInstallationValidationCache
{
    private const int FormatVersion = 1;
    private readonly string _cacheRoot;

    public EngineInstallationValidationCache(string? cacheRoot = null)
    {
        _cacheRoot = Path.GetFullPath(cacheRoot ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Karpik",
            "ValidationCache"));
    }

    /// <summary>Checks whether a previously verified installation still has identical payload metadata.</summary>
    public bool IsCurrent(string installationRoot, EngineInstallationManifest manifest)
    {
        try
        {
            string root = Path.GetFullPath(installationRoot);
            string receiptPath = GetReceiptPath(root);
            if (!File.Exists(receiptPath))
            {
                return false;
            }

            Receipt? receipt = JsonSerializer.Deserialize<Receipt>(File.ReadAllText(receiptPath));
            if (receipt is null || receipt.FormatVersion != FormatVersion ||
                !string.Equals(receipt.InstallationRoot, root, PathSafety.PathComparison) ||
                !string.Equals(receipt.ManifestHash, ComputeFileHash(Path.Combine(root, "engine-installation.json")), StringComparison.Ordinal) ||
                !string.Equals(receipt.ContentHash, manifest.ContentHash, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return receipt.Files.SequenceEqual(CollectPayloadFiles(root));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or InvalidDataException or ArgumentException or NotSupportedException)
        {
            return false;
        }
    }

    /// <summary>Gets the manifest only when the installation still matches a verified receipt.</summary>
    public bool TryGetCurrent(string installationRoot, out EngineInstallationManifest? manifest)
    {
        manifest = null;
        try
        {
            string root = Path.GetFullPath(installationRoot);
            EngineInstallationManifest candidate = EngineInstallationManifest.Parse(
                File.ReadAllText(Path.Combine(root, "engine-installation.json")));
            if (!IsCurrent(root, candidate))
            {
                return false;
            }

            manifest = candidate;
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or ManifestContractException or ArgumentException or NotSupportedException)
        {
            return false;
        }
    }

    /// <summary>Stores metadata only after the caller has completed full cryptographic validation.</summary>
    public void Record(string installationRoot, EngineInstallationManifest manifest)
    {
        string root = Path.GetFullPath(installationRoot);
        var receipt = new Receipt(
            FormatVersion,
            root,
            manifest.ContentHash,
            ComputeFileHash(Path.Combine(root, "engine-installation.json")),
            CollectPayloadFiles(root));
        Directory.CreateDirectory(_cacheRoot);

        string path = GetReceiptPath(root);
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(receipt));
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }

    private string GetReceiptPath(string root)
    {
        string key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(root))).ToLowerInvariant();
        return Path.Combine(_cacheRoot, key + ".json");
    }

    private static FileStamp[] CollectPayloadFiles(string root)
    {
        if (!Directory.Exists(root) || PathSafety.IsReparsePoint(root))
        {
            throw new InvalidDataException($"Installation root is missing or linked: {root}");
        }

        var files = new List<FileStamp>();
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            string directory = pending.Pop();
            if (PathSafety.IsReparsePoint(directory))
            {
                throw new InvalidDataException($"Installation payload contains a linked directory: {directory}");
            }

            foreach (string entry in Directory.EnumerateFileSystemEntries(directory))
            {
                if (!PathSafety.IsContained(root, entry) || PathSafety.IsReparsePoint(entry))
                {
                    throw new InvalidDataException($"Installation payload contains an unsafe entry: {entry}");
                }
                if (Directory.Exists(entry))
                {
                    pending.Push(entry);
                    continue;
                }
                if (!File.Exists(entry))
                {
                    throw new InvalidDataException($"Installation payload contains an unsupported entry: {entry}");
                }

                string relative = Path.GetRelativePath(root, entry).Replace(Path.DirectorySeparatorChar, '/');
                if (relative is "engine-installation.json" or ".complete")
                {
                    continue;
                }
                var file = new FileInfo(entry);
                files.Add(new FileStamp(relative, file.Length, file.LastWriteTimeUtc.Ticks));
            }
        }

        return [.. files.OrderBy(file => file.RelativePath, StringComparer.Ordinal)];
    }

    private static string ComputeFileHash(string path)
    {
        using FileStream stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private sealed record Receipt(
        int FormatVersion,
        string InstallationRoot,
        string ContentHash,
        string ManifestHash,
        FileStamp[] Files);

    private sealed record FileStamp(string RelativePath, long Length, long LastWriteTimeUtcTicks);
}
