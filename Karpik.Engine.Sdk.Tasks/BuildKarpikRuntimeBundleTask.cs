using Microsoft.Build.Framework;
using System.Text;

namespace Karpik.Engine.Sdk.Tasks;

public class RuntimeBundleFileSystem
{
    public virtual void MoveDirectory(string source, string destination) => Directory.Move(source, destination);
    public virtual void DeleteFile(string path) => File.Delete(path);
}

public sealed class BuildKarpikRuntimeBundleTask : Microsoft.Build.Utilities.Task
{
    public const int MaxTreeEntries = 32_768;
    public const int MaxTreeDepth = 64;
    public const int MaxManifestBytes = 1024 * 1024;
    public const int MaxManifestEntries = 4_096;
    public const long MaxIndividualFileBytes = 4L * 1024 * 1024 * 1024;
    public const long MaxBundleBytes = 32L * 1024 * 1024 * 1024;
    public const string BundleCompletionMarker = "karpik-runtime-bundle-v1\n";
    public const string ModuleCompletionMarker = "karpik-module-staging-v1\n";
    public const string SideMarkerPrefix = "karpik-runtime-side-v1:";
    private const string OwnedStagingMarker = "karpik-runtime-owned-staging-v1\n";

    private readonly RuntimeBundleFileSystem _fileSystem;

    public BuildKarpikRuntimeBundleTask() : this(new RuntimeBundleFileSystem()) { }

    public BuildKarpikRuntimeBundleTask(RuntimeBundleFileSystem fileSystem) =>
        _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));

    [Required]
    public string Side { get; set; } = string.Empty;

    /// <summary>
    /// "Dynamic" (default) keeps the versioned managed module staging layout;
    /// "Static" stages executable payload inputs only: content, mods and native
    /// files — never a managed module manifest or module DLLs.
    /// </summary>
    public string CompositionMode { get; set; } = "Dynamic";

    [Required]
    public string PrimaryAssembly { get; set; } = string.Empty;

    [Required]
    public string BundlePath { get; set; } = string.Empty;

    public ITaskItem[] Assemblies { get; set; } = [];

    public ITaskItem[] Content { get; set; } = [];

    public ITaskItem[] Mods { get; set; } = [];

    public ITaskItem[] NativeFiles { get; set; } = [];

    private bool IsStaticMode => IsStaticCompositionMode(CompositionMode);

    private static bool IsStaticCompositionMode(string? compositionMode) =>
        string.Equals(compositionMode ?? "Dynamic", "Static", StringComparison.Ordinal);

    public override bool Execute()
    {
        try
        {
            if (!IsStaticCompositionMode(CompositionMode)
                && !string.Equals(CompositionMode, "Dynamic", StringComparison.Ordinal)
                && !string.IsNullOrEmpty(CompositionMode))
            {
                throw new ArgumentException(
                    $"KarpikCompositionMode must be exactly 'Dynamic' or 'Static'; actual value: '{CompositionMode}'.");
            }

            Publish();
            return true;
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or InvalidDataException or UnauthorizedAccessException or NotSupportedException)
        {
            Log.LogError($"KARPIK006: Runtime bundle publication failed: {exception.Message}");
            return false;
        }
    }

    private void Publish()
    {
        if (Side is not ("Client" or "Server"))
        {
            throw new ArgumentException("KarpikSide must be Client or Server for a runtime bundle.");
        }
        if (!Path.IsPathFullyQualified(BundlePath))
        {
            throw new ArgumentException("KarpikRuntimeBundlePath must be absolute.");
        }

        string destination = TrimRoot(Path.GetFullPath(BundlePath));
        string? parent = Path.GetDirectoryName(destination);
        if (string.IsNullOrEmpty(parent))
        {
            throw new ArgumentException("The runtime bundle destination must have a parent directory.");
        }
        EnsureExistingAncestorsNotReparse(parent);
        Directory.CreateDirectory(parent);
        EnsureNotReparse(parent);

        string backup = destination + ".previous";
        RecoverBackup(destination, backup);
        RecoverOwnedStaging(parent, Path.GetFileName(destination));
        if (Directory.Exists(destination) && !IsProvenBundleOutput(destination))
        {
            throw new InvalidDataException($"Refusing to replace unproven directory '{destination}'.");
        }

        string staging = destination + ".staging." + Guid.NewGuid().ToString("N");
        Directory.CreateDirectory(staging);
        File.WriteAllText(Path.Combine(staging, ".karpik-owned-staging"), OwnedStagingMarker);
        try
        {
            if (IsStaticMode)
            {
                MaterializeStatic(staging);
                if (!IsCompleteStaticBundle(staging, Side, allowOwnershipMarker: true))
                {
                    throw new InvalidDataException("The staged static runtime bundle did not pass completion validation.");
                }
            }
            else
            {
                MaterializeDynamic(staging);
                if (!IsCompleteBundle(staging, Side, allowOwnershipMarker: true, requiredPrimaryAssembly: PrimaryAssembly))
                {
                    throw new InvalidDataException("The staged runtime bundle did not pass completion validation.");
                }
            }

            bool hadDestination = Directory.Exists(destination);
            if (hadDestination)
            {
                _fileSystem.MoveDirectory(destination, backup);
            }
            try
            {
                _fileSystem.MoveDirectory(staging, destination);
                _fileSystem.DeleteFile(Path.Combine(destination, ".karpik-owned-staging"));
            }
            catch
            {
                DeleteOwnedPublishedBundle(destination);
                if (!Directory.Exists(destination) && Directory.Exists(backup) && IsProvenBundleOutput(backup))
                {
                    _fileSystem.MoveDirectory(backup, destination);
                }
                throw;
            }

            if (Directory.Exists(backup) && IsProvenBundleOutput(backup))
            {
                Directory.Delete(backup, recursive: true);
            }
        }
        finally
        {
            DeleteOwnedStaging(staging);
        }
    }

    private void MaterializeDynamic(string staging)
    {
        if (!Path.IsPathFullyQualified(PrimaryAssembly) || !File.Exists(PrimaryAssembly))
        {
            throw new FileNotFoundException("The primary game assembly must be an existing absolute path.", PrimaryAssembly);
        }
        if (IsReparsePoint(PrimaryAssembly))
        {
            throw new InvalidDataException($"The primary game assembly is a link or reparse point: {PrimaryAssembly}");
        }
        if (Content.Length == 0)
        {
            throw new InvalidDataException("A runtime bundle must contain at least one content item.");
        }
        if (Assemblies.Length + 1 > MaxManifestEntries)
        {
            throw new InvalidDataException($"Runtime bundle exceeds the maximum of {MaxManifestEntries} assemblies.");
        }
        if (Mods.Length > MaxTreeEntries || Content.Length > MaxTreeEntries - Mods.Length)
        {
            throw new InvalidDataException($"Runtime bundle exceeds the maximum of {MaxTreeEntries} asset items.");
        }
        var sources = new SortedDictionary<string, string>(StringComparer.Ordinal);
        var assemblyNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        AddAssembly(sources, assemblyNames, PrimaryAssembly);
        foreach (ITaskItem item in Assemblies)
        {
            AddAssembly(sources, assemblyNames, item.ItemSpec);
        }

        string manifestText = string.Join('\n', sources.Keys) + '\n';
        if (Encoding.UTF8.GetByteCount(manifestText) > MaxManifestBytes)
        {
            throw new InvalidDataException($"Runtime module manifest exceeds {MaxManifestBytes} bytes.");
        }

        long totalInputBytes = Encoding.UTF8.GetByteCount(manifestText)
                               + Encoding.UTF8.GetByteCount(ModuleCompletionMarker)
                               + Encoding.UTF8.GetByteCount(BundleCompletionMarker)
                               + Encoding.UTF8.GetByteCount(SideMarkerPrefix + Side + "\n")
                               + Encoding.UTF8.GetByteCount(OwnedStagingMarker);
        foreach (string source in sources.Values)
        {
            long length = new FileInfo(source).Length;
            if (length > MaxIndividualFileBytes || totalInputBytes > MaxBundleBytes - length)
            {
                throw new InvalidDataException(
                    $"Runtime assembly inputs exceed bundle limits ({MaxIndividualFileBytes} bytes per file, {MaxBundleBytes} bytes total).");
            }
            totalInputBytes += length;
        }

        string modules = Path.Combine(staging, "modules.version.1");
        string contentRoot = Path.Combine(staging, "Content");
        var assetDirectories = new HashSet<string>(BundleIdentityComparer);
        var assetSources = new List<(string Source, string Destination)>();
        PrepareAssetRoot(Content, "Content", contentRoot, assetDirectories, assetSources, ref totalInputBytes);
        if (Mods.Length > 0)
        {
            PrepareAssetRoot(
                Mods,
                "Mods",
                Path.Combine(staging, "Mods"),
                assetDirectories,
                assetSources,
                ref totalInputBytes);
        }

        int fixedEntries = 3 // root markers plus staging ownership marker
                           + 1 // modules.version.1 directory
                           + sources.Count
                           + 2; // module manifest and marker
        if (fixedEntries > MaxTreeEntries - assetDirectories.Count - assetSources.Count)
        {
            throw new InvalidDataException($"Runtime bundle exceeds the maximum of {MaxTreeEntries} tree entries.");
        }

        Directory.CreateDirectory(modules);
        Directory.CreateDirectory(contentRoot);
        foreach ((string fileName, string source) in sources)
        {
            File.Copy(source, Path.Combine(modules, fileName), overwrite: false);
        }
        File.WriteAllText(
            Path.Combine(modules, "modules.list"),
            manifestText,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        File.WriteAllText(Path.Combine(modules, ".complete"), ModuleCompletionMarker);
        foreach ((string source, string destination) in assetSources)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(source, destination, overwrite: false);
        }

        File.WriteAllText(Path.Combine(staging, "runtime-bundle.side"), SideMarkerPrefix + Side + "\n");
        File.WriteAllText(Path.Combine(staging, ".complete"), BundleCompletionMarker);
    }

    private void MaterializeStatic(string staging)
    {
        if (Content.Length == 0)
        {
            throw new InvalidDataException("A static runtime bundle must contain at least one content item.");
        }
        if (Mods.Length > MaxTreeEntries || Content.Length > MaxTreeEntries - Mods.Length)
        {
            throw new InvalidDataException($"Static runtime bundle exceeds the maximum of {MaxTreeEntries} asset items.");
        }

        var sources = new List<(string Source, string Destination)>();
        var directories = new HashSet<string>(BundleIdentityComparer);
        long totalInputBytes = Encoding.UTF8.GetByteCount(BundleCompletionMarker)
                               + Encoding.UTF8.GetByteCount(SideMarkerPrefix + Side + "\n")
                               + Encoding.UTF8.GetByteCount(OwnedStagingMarker);
        PrepareAssetRoot(Content, "Content", Path.Combine(staging, "Content"), directories, sources, ref totalInputBytes);
        if (Mods.Length > 0)
        {
            PrepareAssetRoot(
                Mods,
                "Mods",
                Path.Combine(staging, "Mods"),
                directories,
                sources,
                ref totalInputBytes);
        }
        foreach (ITaskItem item in NativeFiles.OrderBy(item => item.GetMetadata("TargetPath"), StringComparer.Ordinal))
        {
            string source = Path.GetFullPath(item.ItemSpec);
            string relative = item.GetMetadata("TargetPath");
            if (string.IsNullOrWhiteSpace(relative))
            {
                throw new InvalidDataException(
                    $"A static runtime bundle native input requires a relative TargetPath: {item.ItemSpec}");
            }
            relative = relative.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
            if (!IsNativeRelativePath(relative))
            {
                throw new InvalidDataException(
                    $"A static runtime bundle native TargetPath must stay under native/ or runtimes/: {relative}");
            }
            string destination = Path.GetFullPath(Path.Combine(staging, relative));
            if (!IsContained(staging, destination))
            {
                throw new InvalidDataException($"A static runtime bundle native target escapes the bundle root: {relative}");
            }
            long length = new FileInfo(source).Length;
            if (!File.Exists(source) || IsReparsePoint(source) || length > MaxIndividualFileBytes
                || totalInputBytes > MaxBundleBytes - length)
            {
                throw new InvalidDataException(
                    $"Static runtime bundle native input is missing, linked, or exceeds bundle limits: {item.ItemSpec}");
            }
            totalInputBytes += length;
            for (string? directory = Path.GetDirectoryName(destination);
                 directory is not null && IsContained(staging, directory);
                 directory = Path.GetDirectoryName(directory))
            {
                directories.Add(directory);
            }
            sources.Add((source, destination));
        }

        int fixedEntries = 3; // completion marker, side marker, staging ownership marker
        if (fixedEntries + directories.Count + sources.Count
            > MaxTreeEntries)
        {
            throw new InvalidDataException($"Static runtime bundle exceeds the maximum of {MaxTreeEntries} tree entries.");
        }

        foreach ((string source, string destination) in sources)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(source, destination, overwrite: false);
        }

        File.WriteAllText(Path.Combine(staging, "runtime-bundle.side"), SideMarkerPrefix + Side + "\n");
        File.WriteAllText(Path.Combine(staging, ".complete"), BundleCompletionMarker);
    }

    private static bool IsNativeRelativePath(string relativePath)
    {
        string firstSegment = relativePath.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries)[0];
        return firstSegment is "native" or "runtimes";
    }

    private static void PrepareAssetRoot(
        IEnumerable<ITaskItem> items,
        string rootName,
        string assetRoot,
        ISet<string> assetDirectories,
        ICollection<(string Source, string Destination)> assetSources,
        ref long totalInputBytes)
    {
        var targets = new HashSet<string>(BundleIdentityComparer);
        assetDirectories.Add(assetRoot);
        foreach (ITaskItem item in items.OrderBy(item => item.GetMetadata("TargetPath"), StringComparer.Ordinal))
        {
            string source = Path.GetFullPath(item.ItemSpec);
            if (!File.Exists(source) || IsReparsePoint(source))
            {
                throw new InvalidDataException($"Runtime {rootName} item is missing or linked: {item.ItemSpec}");
            }
            EnsureNotReparse(Path.GetDirectoryName(source)!);
            string relative = item.GetMetadata("TargetPath");
            if (string.IsNullOrWhiteSpace(relative))
            {
                relative = Path.GetFileName(source);
            }
            relative = relative.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
            string destination = Path.GetFullPath(Path.Combine(assetRoot, relative));
            if (!IsContained(assetRoot, destination) || !targets.Add(destination))
            {
                throw new InvalidDataException($"Runtime asset target escapes or conflicts within {rootName}: {relative}");
            }
            int depth = Path.GetRelativePath(assetRoot, destination)
                .Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries)
                .Length;
            long length = new FileInfo(source).Length;
            if (depth + 1 > MaxTreeDepth
                || length > MaxIndividualFileBytes
                || totalInputBytes > MaxBundleBytes - length)
            {
                throw new InvalidDataException(
                    $"Runtime {rootName} item exceeds bundle limits ({MaxTreeDepth} depth, {MaxIndividualFileBytes} bytes per file, {MaxBundleBytes} bytes total): {relative}");
            }
            totalInputBytes += length;
            for (string? directory = Path.GetDirectoryName(destination);
                 directory is not null && IsContained(assetRoot, directory);
                 directory = Path.GetDirectoryName(directory))
            {
                assetDirectories.Add(directory);
                if (BundleIdentityComparer.Equals(directory, assetRoot))
                {
                    break;
                }
            }
            assetSources.Add((source, destination));
        }
    }

    private static void AddAssembly(
        IDictionary<string, string> sources,
        ISet<string> assemblyNames,
        string path)
    {
        if (!Path.IsPathFullyQualified(path) || !File.Exists(path) || IsReparsePoint(path))
        {
            throw new InvalidDataException($"Runtime assembly is missing, relative, or linked: {path}");
        }
        string fileName = Path.GetFileName(path);
        if (!fileName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"Runtime assembly must be a .dll: {path}");
        }
        if (fileName.StartsWith("Karpik.Engine.Core.Runner", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("The engine runner must never be copied into a game-owned bundle.");
        }
        string fullPath = Path.GetFullPath(path);
        EnsureNotReparse(Path.GetDirectoryName(fullPath)!);
        if (sources.TryGetValue(fileName, out string? existing))
        {
            if (!PathComparer.Equals(existing, fullPath))
            {
                throw new InvalidDataException($"Two runtime assemblies have the same file name: {fileName}");
            }
            return;
        }
        if (!assemblyNames.Add(fileName))
        {
            throw new InvalidDataException($"Two runtime assemblies have the same file name: {fileName}");
        }
        sources[fileName] = fullPath;
    }

    private bool IsProvenBundleOutput(string path)
    {
        return Directory.Exists(path) && (
            IsCompleteBundle(path, Side, allowOwnershipMarker: false, requiredPrimaryAssembly: null)
            || IsCompleteStaticBundle(path, Side, allowOwnershipMarker: false));
    }

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

            var allowedRootFiles = new HashSet<string>(StringComparer.Ordinal)
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
            var allowedRootDirectories = new HashSet<string>(StringComparer.Ordinal)
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
                var info = new FileInfo(file);
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

    private void RecoverBackup(string destination, string backup)
    {
        if (!Directory.Exists(backup))
        {
            return;
        }
        if (IsReparsePoint(backup) || !IsProvenBundleOutput(backup))
        {
            throw new InvalidDataException($"Refusing to move or delete unproven interrupted backup '{backup}'.");
        }
        if (!Directory.Exists(destination))
        {
            _fileSystem.MoveDirectory(backup, destination);
            return;
        }
        if (!IsProvenBundleOutput(destination))
        {
            throw new InvalidDataException($"Interrupted bundle state contains an invalid destination '{destination}'.");
        }
        Directory.Delete(backup, recursive: true);
    }

    private static void RecoverOwnedStaging(string parent, string destinationName)
    {
        int count = 0;
        foreach (string candidate in Directory.EnumerateDirectories(parent, destinationName + ".staging.*", SearchOption.TopDirectoryOnly))
        {
            if (++count > MaxTreeEntries)
            {
                throw new InvalidDataException($"Runtime bundle staging recovery exceeds the {MaxTreeEntries} entry limit.");
            }
            DeleteOwnedStaging(candidate);
        }
    }

    private static void DeleteOwnedStaging(string path)
    {
        if (!Directory.Exists(path) || IsReparsePoint(path))
        {
            return;
        }
        string marker = Path.Combine(path, ".karpik-owned-staging");
        if (IsBoundedTreeWithoutLinks(path) && HasExactUtf8File(marker, OwnedStagingMarker))
        {
            Directory.Delete(path, recursive: true);
        }
    }

    private void DeleteOwnedPublishedBundle(string path)
    {
        if (!Directory.Exists(path) || IsReparsePoint(path))
        {
            return;
        }
        string marker = Path.Combine(path, ".karpik-owned-staging");
        bool complete = IsStaticMode
            ? IsCompleteStaticBundle(path, Side, allowOwnershipMarker: true)
            : IsCompleteBundle(path, Side, allowOwnershipMarker: true, requiredPrimaryAssembly: PrimaryAssembly);
        if (complete && HasExactUtf8File(marker, OwnedStagingMarker))
        {
            Directory.Delete(path, recursive: true);
        }
    }

    private static bool IsCompleteBundle(
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
                || (requiredPrimaryAssembly is not null
                    && !names.Contains(Path.GetFileName(requiredPrimaryAssembly), StringComparer.OrdinalIgnoreCase)))
            {
                return false;
            }

            var allowedRootFiles = new HashSet<string>(StringComparer.Ordinal)
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
            var allowedRootDirectories = new HashSet<string>(StringComparer.Ordinal)
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

            var listed = new HashSet<string>(names, BundleIdentityComparer);
            var actual = new HashSet<string>(BundleIdentityComparer);
            foreach (string entry in Directory.EnumerateFileSystemEntries(modules, "*", SearchOption.TopDirectoryOnly))
            {
                if (Directory.Exists(entry))
                {
                    return false;
                }
                string name = Path.GetFileName(entry);
                if (name is ".complete" or "modules.list")
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
                var info = new FileInfo(file);
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
        var unique = new HashSet<string>(BundleIdentityComparer);
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

    private static bool HasExactUtf8File(string path, string expected)
    {
        if (!File.Exists(path))
        {
            return false;
        }
        byte[] expectedBytes = Encoding.UTF8.GetBytes(expected);
        var info = new FileInfo(path);
        return info.Length == expectedBytes.Length
               && File.ReadAllBytes(path).AsSpan().SequenceEqual(expectedBytes);
    }

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

    private static bool IsBoundedTreeWithoutLinks(string root)
    {
        try
        {
            int count = 0;
            var pending = new Stack<(string Directory, int Depth)>();
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

    private static IEnumerable<string> EnumerateFilesBounded(string root)
    {
        int count = 0;
        var pending = new Stack<(string Directory, int Depth)>();
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

    private static void EnsureNotReparse(string path)
    {
        var current = new DirectoryInfo(Path.GetFullPath(path));
        while (current is not null)
        {
            if ((current.Attributes & FileAttributes.ReparsePoint) != 0 || current.LinkTarget is not null)
            {
                throw new InvalidDataException($"Runtime bundle path contains a link or reparse point: {current.FullName}");
            }
            current = current.Parent;
        }
    }

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

    private static bool IsContained(string root, string candidate)
    {
        string prefix = TrimRoot(Path.GetFullPath(root)) + Path.DirectorySeparatorChar;
        return Path.GetFullPath(candidate).StartsWith(prefix, PathComparison);
    }

    private static string TrimRoot(string path) => path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    private static bool IsReparsePoint(string path)
    {
        var info = Directory.Exists(path) ? (FileSystemInfo)new DirectoryInfo(path) : new FileInfo(path);
        return (info.Attributes & FileAttributes.ReparsePoint) != 0 || info.LinkTarget is not null;
    }

    private static StringComparison PathComparison => OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
    private static StringComparer PathComparer => OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
    private static StringComparer BundleIdentityComparer => StringComparer.OrdinalIgnoreCase;
}
