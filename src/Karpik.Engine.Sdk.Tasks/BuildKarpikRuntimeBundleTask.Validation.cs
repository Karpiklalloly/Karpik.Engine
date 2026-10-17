using System.Text;
using Karpik.Engine.Tooling;

namespace Karpik.Engine.Sdk.Tasks;

public sealed partial class BuildKarpikRuntimeBundleTask
{
    /// <summary>Проверяет структуру и маркеры завершённого static bundle.</summary>
    private bool IsCompleteStaticBundle(string root, string side, bool allowOwnershipMarker)
    {
        try
        {
            if (!Directory.Exists(root) || !IsBoundedTreeWithoutLinks(root))
            {
                return false;
            }
            if (!HasExactUtf8File(Path.Combine(root, ".complete"), BundleCompletionMarker)
                || !HasExactUtf8File(Path.Combine(root, "runtime-bundle.side"), SideMarkerPrefix + side + "\n"))
            {
                return false;
            }

            HashSet<string> allowedRootFiles = new HashSet<string>(StringComparer.Ordinal)
            {
                ".complete", "runtime-bundle.side"
            };
            if (allowOwnershipMarker)
            {
                allowedRootFiles.Add(".karpik-owned-staging");
                if (!HasExactUtf8File(Path.Combine(root, ".karpik-owned-staging"), OwnedStagingMarker))
                {
                    return false;
                }
            }
            else if (File.Exists(Path.Combine(root, ".karpik-owned-staging")))
            {
                return false;
            }

            // A static runtime bundle never carries managed module staging, a
            // module manifest or a shadow directory. Native payload inputs live
            // only under native/ or runtimes/.
            HashSet<string> allowedRootDirectories = new HashSet<string>(StringComparer.Ordinal)
            {
                "Content", "Mods", "reload", "native", "runtimes"
            };
            foreach (string entry in Directory.EnumerateFileSystemEntries(root, "*", SearchOption.TopDirectoryOnly))
            {
                string name = Path.GetFileName(entry);
                if (Directory.Exists(entry) ? !allowedRootDirectories.Contains(name) : !allowedRootFiles.Contains(name))
                {
                    return false;
                }
            }
            if (!ValidateReloadShape(Path.Combine(root, "reload")))
            {
                return false;
            }

            string content = Path.Combine(root, "Content");
            long totalBytes = 0;
            bool hasContent = false;
            foreach (string file in EnumerateFilesBounded(root))
            {
                string name = Path.GetFileName(file);
                if (name == "modules.list")
                {
                    // A static runtime output never carries or reads a managed module manifest.
                    return false;
                }
                FileInfo info = new FileInfo(file);
                if (info.Length > MaxIndividualFileBytes
                    || totalBytes > MaxBundleBytes - info.Length)
                {
                    return false;
                }
                totalBytes += info.Length;
                hasContent |= IsContained(content, file);
            }
            return hasContent;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or DecoderFallbackException or InvalidDataException)
        {
            return false;
        }
    }

    /// <summary>Проверяет структуру, manifest и размеры завершённого dynamic bundle.</summary>
    private bool IsCompleteBundle(
        string root,
        string side,
        bool allowOwnershipMarker,
        string? requiredPrimaryAssembly)
    {
        try
        {
            if (!Directory.Exists(root) || !IsBoundedTreeWithoutLinks(root))
            {
                return false;
            }
            string modules = Path.Combine(root, "modules.version.1");
            string content = Path.Combine(root, "Content");
            if (!HasExactUtf8File(Path.Combine(root, ".complete"), BundleCompletionMarker)
                || !HasExactUtf8File(Path.Combine(root, "runtime-bundle.side"), SideMarkerPrefix + side + "\n")
                || !Directory.Exists(content)
                || !Directory.Exists(modules)
                || !HasExactUtf8File(Path.Combine(modules, ".complete"), ModuleCompletionMarker)
                || !TryReadCanonicalManifest(Path.Combine(modules, "modules.list"), out string[] names)
                || (RequireEngineModuleSelection
                    && !TryReadCanonicalEngineManifest(Path.Combine(modules, EngineModuleManifestFileName), out _))
                || (requiredPrimaryAssembly is not null
                    && !names.Contains(Path.GetFileName(requiredPrimaryAssembly), StringComparer.OrdinalIgnoreCase)))
            {
                return false;
            }

            HashSet<string> allowedRootFiles = new HashSet<string>(StringComparer.Ordinal)
            {
                ".complete", "runtime-bundle.side"
            };
            if (allowOwnershipMarker)
            {
                allowedRootFiles.Add(".karpik-owned-staging");
                if (!HasExactUtf8File(Path.Combine(root, ".karpik-owned-staging"), OwnedStagingMarker))
                {
                    return false;
                }
            }
            HashSet<string> allowedRootDirectories = new HashSet<string>(StringComparer.Ordinal)
            {
                "Content", "Mods", "modules.version.1", "reload"
            };
            foreach (string entry in Directory.EnumerateFileSystemEntries(root, "*", SearchOption.TopDirectoryOnly))
            {
                string name = Path.GetFileName(entry);
                if (Directory.Exists(entry) ? !allowedRootDirectories.Contains(name) : !allowedRootFiles.Contains(name))
                {
                    return false;
                }
            }
            if (!allowOwnershipMarker && File.Exists(Path.Combine(root, ".karpik-owned-staging")))
            {
                return false;
            }
            if (!ValidateReloadShape(Path.Combine(root, "reload")))
            {
                return false;
            }

            HashSet<string> listed = new HashSet<string>(names, BundleIdentityComparer);
            HashSet<string> actual = new HashSet<string>(BundleIdentityComparer);
            foreach (string entry in Directory.EnumerateFileSystemEntries(modules, "*", SearchOption.TopDirectoryOnly))
            {
                if (Directory.Exists(entry))
                {
                    return false;
                }
                string name = Path.GetFileName(entry);
                if (name is ".complete" or "modules.list" or EngineModuleManifestFileName)
                {
                    continue;
                }
                if (!name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
                    || name.StartsWith("Karpik.Engine.Core.Runner", StringComparison.OrdinalIgnoreCase)
                    || !actual.Add(name))
                {
                    return false;
                }
            }
            if (!actual.SetEquals(listed))
            {
                return false;
            }

            bool hasContent = false;
            long totalBytes = 0;
            foreach (string file in EnumerateFilesBounded(root))
            {
                FileInfo info = new FileInfo(file);
                if (info.Length > MaxIndividualFileBytes
                    || totalBytes > MaxBundleBytes - info.Length)
                {
                    return false;
                }
                totalBytes += info.Length;
                hasContent |= IsContained(content, file);
            }
            return hasContent;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or DecoderFallbackException)
        {
            return false;
        }
    }

    /// <summary>Читает и проверяет канонический список DLL из manifest-файла модулей.</summary>
    private static bool TryReadCanonicalManifest(string path, out string[] names)
    {
        names = [];
        if (!File.Exists(path))
        {
            return false;
        }
        long length = new FileInfo(path).Length;
        if (length is <= 0 or > MaxManifestBytes)
        {
            return false;
        }
        byte[] bytes = File.ReadAllBytes(path);
        if (bytes.Length != length || bytes.AsSpan().StartsWith(Encoding.UTF8.Preamble))
        {
            return false;
        }
        string text = new UTF8Encoding(false, true).GetString(bytes);
        if (!text.EndsWith('\n') || text.Contains('\r'))
        {
            return false;
        }
        string body = text[..^1];
        if (body.Length == 0)
        {
            return false;
        }
        names = body.Split('\n');
        if (names.Length > MaxManifestEntries
            || !names.SequenceEqual(names.Order(StringComparer.Ordinal), StringComparer.Ordinal))
        {
            return false;
        }
        HashSet<string> unique = new HashSet<string>(BundleIdentityComparer);
        foreach (string name in names)
        {
            if (name.Length == 0
                || name != Path.GetFileName(name)
                || !name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
                || name.StartsWith("Karpik.Engine.Core.Runner", StringComparison.OrdinalIgnoreCase)
                || !unique.Add(name))
            {
                return false;
            }
        }
        return true;
    }

    private static bool TryReadCanonicalEngineManifest(string path, out string[] ids)
    {
        ids = [];
        if (!File.Exists(path))
        {
            return false;
        }
        long length = new FileInfo(path).Length;
        if (length is <= 0 or > MaxManifestBytes)
        {
            return false;
        }
        byte[] bytes = File.ReadAllBytes(path);
        if (bytes.Length != length || bytes.AsSpan().StartsWith(Encoding.UTF8.Preamble))
        {
            return false;
        }
        string text = new UTF8Encoding(false, true).GetString(bytes);
        if (!text.EndsWith('\n') || text.Contains('\r'))
        {
            return false;
        }
        ids = text[..^1].Split('\n');
        HashSet<string> unique = new HashSet<string>(ModuleLayoutPolicy.ModuleIdComparer);
        if (ids.Length is 0 or > MaxManifestEntries
            || !ids.SequenceEqual(ids.Order(StringComparer.Ordinal), StringComparer.Ordinal)
            || ids.Any(id => !ModuleLayoutPolicy.IsSafeModuleId(id) || !unique.Add(id)))
        {
            ids = [];
            return false;
        }
        return bytes.AsSpan().SequenceEqual(Encoding.UTF8.GetBytes(string.Join('\n', ids) + '\n'));
    }

    /// <summary>Проверяет, что UTF-8 файл содержит в точности ожидаемую строку.</summary>
    private static bool HasExactUtf8File(string path, string expected)
    {
        if (!File.Exists(path))
        {
            return false;
        }
        byte[] expectedBytes = Encoding.UTF8.GetBytes(expected);
        FileInfo info = new FileInfo(path);
        return info.Length == expectedBytes.Length
               && File.ReadAllBytes(path).AsSpan().SequenceEqual(expectedBytes);
    }

    /// <summary>Проверяет допустимую форму необязательного каталога hot-reload.</summary>
    private static bool ValidateReloadShape(string reload)
    {
        if (!Directory.Exists(reload))
        {
            return true;
        }
        foreach (string entry in Directory.EnumerateFileSystemEntries(reload, "*", SearchOption.TopDirectoryOnly))
        {
            if (!Directory.Exists(entry) || Path.GetFileName(entry) is not ("state" or "shadow"))
            {
                return false;
            }
        }
        return true;
    }

    /// <summary>Проверяет ограничение размера дерева и отсутствие ссылок в нём.</summary>
    private static bool IsBoundedTreeWithoutLinks(string root)
    {
        try
        {
            int count = 0;
            Stack<(string Directory, int Depth)> pending = new Stack<(string Directory, int Depth)>();
            pending.Push((root, 0));
            while (pending.Count > 0)
            {
                (string directory, int depth) = pending.Pop();
                if (depth > MaxTreeDepth || IsReparsePoint(directory))
                {
                    return false;
                }
                foreach (string entry in Directory.EnumerateFileSystemEntries(directory, "*", SearchOption.TopDirectoryOnly))
                {
                    if (++count > MaxTreeEntries || IsReparsePoint(entry))
                    {
                        return false;
                    }
                    if (Directory.Exists(entry))
                    {
                        pending.Push((entry, depth + 1));
                    }
                }
            }
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>Перечисляет файлы дерева, останавливаясь при превышении его лимитов.</summary>
    private static IEnumerable<string> EnumerateFilesBounded(string root)
    {
        int count = 0;
        Stack<(string Directory, int Depth)> pending = new Stack<(string Directory, int Depth)>();
        pending.Push((root, 0));
        while (pending.Count > 0)
        {
            (string directory, int depth) = pending.Pop();
            if (depth > MaxTreeDepth)
            {
                throw new InvalidDataException($"Runtime bundle exceeds the maximum depth of {MaxTreeDepth}.");
            }
            foreach (string entry in Directory.EnumerateFileSystemEntries(directory, "*", SearchOption.TopDirectoryOnly))
            {
                if (++count > MaxTreeEntries)
                {
                    throw new InvalidDataException($"Runtime bundle exceeds the maximum of {MaxTreeEntries} entries.");
                }
                if (Directory.Exists(entry))
                {
                    pending.Push((entry, depth + 1));
                }
                else
                {
                    yield return entry;
                }
            }
        }
    }

    /// <summary>Отклоняет путь, содержащий symbolic link или reparse point.</summary>
    private static void EnsureNotReparse(string path)
    {
        DirectoryInfo? current = new DirectoryInfo(Path.GetFullPath(path));
        while (current is not null)
        {
            if ((current.Attributes & FileAttributes.ReparsePoint) != 0 || current.LinkTarget is not null)
            {
                throw new InvalidDataException($"Runtime bundle path contains a link or reparse point: {current.FullName}");
            }
            current = current.Parent;
        }
    }

    /// <summary>Проверяет существующих предков пути до создания нового каталога.</summary>
    private static void EnsureExistingAncestorsNotReparse(string path)
    {
        DirectoryInfo? current = new DirectoryInfo(Path.GetFullPath(path));
        while (current is not null && !current.Exists)
        {
            current = current.Parent;
        }
        if (current is null)
        {
            throw new InvalidDataException($"Runtime bundle path has no existing ancestor: {path}");
        }
        EnsureNotReparse(current.FullName);
    }

    /// <summary>Проверяет, находится ли путь внутри заданного корня.</summary>
    private static bool IsContained(string root, string candidate)
    {
        string prefix = TrimRoot(Path.GetFullPath(root)) + Path.DirectorySeparatorChar;
        return Path.GetFullPath(candidate).StartsWith(prefix, PathComparison);
    }

    /// <summary>Удаляет завершающие разделители каталога.</summary>
    private static string TrimRoot(string path) => path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    /// <summary>Определяет, является ли путь ссылкой или reparse point.</summary>
    private static bool IsReparsePoint(string path)
    {
        FileSystemInfo info = Directory.Exists(path) ? (FileSystemInfo)new DirectoryInfo(path) : new FileInfo(path);
        return (info.Attributes & FileAttributes.ReparsePoint) != 0 || info.LinkTarget is not null;
    }

    /// <summary>Получает платформенно-зависимое правило сравнения путей.</summary>
    private static StringComparison PathComparison => OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
    /// <summary>Получает платформенно-зависимый comparer путей.</summary>
    private static StringComparer PathComparer => OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
    /// <summary>Получает comparer идентичностей файлов bundle без учёта регистра.</summary>
    private static StringComparer BundleIdentityComparer => StringComparer.OrdinalIgnoreCase;
}
