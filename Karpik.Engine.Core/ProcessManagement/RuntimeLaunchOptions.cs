using System.Text;

namespace Karpik.Engine.Core;

public sealed record RuntimeLaunchOptions
{
    public Side Side { get; }
    public string RunnerExecutablePath { get; }
    public string BundlePath { get; }
    public string EngineRoot { get; }

    public RuntimeLaunchOptions(Side side, string runnerExecutablePath, string bundlePath, string engineRoot)
    {
        if (side is not (Side.Client or Side.Server))
        {
            throw new ArgumentOutOfRangeException(nameof(side), side, "Runtime side must be Client or Server.");
        }
        RunnerExecutablePath = ValidateExistingAbsoluteFile(runnerExecutablePath, nameof(runnerExecutablePath));
        BundlePath = RuntimeBundleLayout.ValidateAny(bundlePath, side);
        EngineRoot = ValidateExistingAbsoluteDirectory(engineRoot, nameof(engineRoot));
        Side = side;
    }

    private static string ValidateExistingAbsoluteFile(string path, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path))
        {
            throw new ArgumentException("Runner executable path must be absolute.", parameterName);
        }
        string fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException("Runner executable was not found.", fullPath);
        }
        RuntimeBundleLayout.EnsureExistingPathHasNoReparsePoints(fullPath);
        return fullPath;
    }

    private static string ValidateExistingAbsoluteDirectory(string path, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path))
        {
            throw new ArgumentException("Engine root path must be absolute.", parameterName);
        }
        string fullPath = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (!Directory.Exists(fullPath))
        {
            throw new DirectoryNotFoundException($"Engine root was not found: {fullPath}");
        }
        RuntimeBundleLayout.EnsureExistingPathHasNoReparsePoints(fullPath);
        return fullPath;
    }
}

public static class RuntimeBundleLayout
{
    // Keep these values synchronized with BuildKarpikRuntimeBundleTask. The SDK task cannot reference Core.
    public const int MaxTreeEntries = 32_768;
    public const int MaxTreeDepth = 64;
    public const int MaxManifestBytes = 1024 * 1024;
    public const int MaxManifestEntries = 4_096;
    public const long MaxIndividualFileBytes = 4L * 1024 * 1024 * 1024;
    public const long MaxBundleBytes = 32L * 1024 * 1024 * 1024;
    public const string BundleCompletionMarker = "karpik-runtime-bundle-v1\n";
    public const string ModuleCompletionMarker = "karpik-module-staging-v1\n";
    public const string EngineModuleManifestFileName = "engine.modules.list";
    public const string SideMarkerPrefix = "karpik-runtime-side-v1:";

    private static readonly StringComparer PathComparer = OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;

    public static string Validate(string bundlePath, Side side)
    {
        if (string.IsNullOrWhiteSpace(bundlePath) || !Path.IsPathFullyQualified(bundlePath))
        {
            throw new ArgumentException("Runtime bundle path must be absolute.", nameof(bundlePath));
        }
        string root = Path.GetFullPath(bundlePath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (!Directory.Exists(root))
        {
            throw new DirectoryNotFoundException($"Runtime bundle does not exist: {root}");
        }

        EnsureExistingPathHasNoReparsePoints(root);
        EnsureTreeIsBoundedAndHasNoReparsePoints(root);
        RequireExact(Path.Combine(root, ".complete"), BundleCompletionMarker, "bundle completion marker");
        RequireExact(Path.Combine(root, "runtime-bundle.side"), SideMarkerPrefix + side + "\n", "runtime side marker");
        ValidateRootShape(root);

        string content = Path.Combine(root, "Content");
        if (!Directory.Exists(content) || !Directory.EnumerateFiles(content, "*", SearchOption.AllDirectories).Any())
        {
            throw new InvalidDataException("Runtime bundle Content is missing or empty.");
        }

        string modules = ResolveModuleDirectory(root);
        ReadCanonicalModuleManifest(modules);
        if (File.Exists(Path.Combine(modules, EngineModuleManifestFileName)))
        {
            ReadCanonicalEngineModuleManifest(modules);
        }
        return root;
    }

    /// <summary>
    /// Validates a Dynamic or Static bundle by its layout shape: bundles that carry
    /// the versioned managed module directory keep the canonical manifest rules,
    /// manifest-free bundles are validated as static runtime outputs.
    /// </summary>
    public static string ValidateAny(string bundlePath, Side side)
    {
        if (string.IsNullOrWhiteSpace(bundlePath) || !Path.IsPathFullyQualified(bundlePath))
        {
            throw new ArgumentException("Runtime bundle path must be absolute.", nameof(bundlePath));
        }
        string root = Path.GetFullPath(bundlePath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (!Directory.Exists(root))
        {
            throw new DirectoryNotFoundException($"Runtime bundle does not exist: {root}");
        }

        foreach (string directory in Directory.EnumerateDirectories(root, "modules.version.*", SearchOption.TopDirectoryOnly))
        {
            if (PathComparer.Equals(Path.GetFileName(directory), "modules.version.1"))
            {
                return Validate(root, side);
            }

            throw new InvalidDataException(
                $"Runtime bundle must contain only the exact module directory modules.version.1; found '{Path.GetFileName(directory)}'.");
        }

        return ValidateStatic(root, side);
    }

    /// <summary>
    /// Validates a manifest-free Static bundle layout: executable payload inputs
    /// only — content, mods, reload state and native files under native/ or
    /// runtimes/. Managed module staging and manifests are rejected.
    /// </summary>
    public static string ValidateStatic(string bundlePath, Side side)
    {
        if (string.IsNullOrWhiteSpace(bundlePath) || !Path.IsPathFullyQualified(bundlePath))
        {
            throw new ArgumentException("Runtime bundle path must be absolute.", nameof(bundlePath));
        }
        string root = Path.GetFullPath(bundlePath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (!Directory.Exists(root))
        {
            throw new DirectoryNotFoundException($"Runtime bundle does not exist: {root}");
        }

        EnsureExistingPathHasNoReparsePoints(root);
        EnsureTreeIsBoundedAndHasNoReparsePoints(root);
        RequireExact(Path.Combine(root, ".complete"), BundleCompletionMarker, "bundle completion marker");
        RequireExact(Path.Combine(root, "runtime-bundle.side"), SideMarkerPrefix + side + "\n", "runtime side marker");

        foreach (string entry in Directory.EnumerateFileSystemEntries(root))
        {
            string name = Path.GetFileName(entry);
            bool allowed = File.Exists(entry)
                ? name is ".complete" or "runtime-bundle.side"
                : name is "Content" or "Mods" or "reload" or "native" or "runtimes";
            if (!allowed)
            {
                throw new InvalidDataException(
                    $"Static runtime bundle contains an unexpected root entry: {name}");
            }
        }

        ValidateReloadDirectoryShape(root);

        foreach (string file in EnumerateFilesBounded(root))
        {
            if (Path.GetFileName(file) == "modules.list")
            {
                throw new InvalidDataException("A static runtime bundle must not contain a managed module manifest.");
            }
        }

        string content = Path.Combine(root, "Content");
        if (!Directory.Exists(content) || !Directory.EnumerateFiles(content, "*", SearchOption.AllDirectories).Any())
        {
            throw new InvalidDataException("Static runtime bundle Content is missing or empty.");
        }

        return root;
    }

    private static void ValidateReloadDirectoryShape(string root)
    {
        string reload = Path.Combine(root, "reload");
        if (!Directory.Exists(reload))
        {
            return;
        }
        foreach (string entry in Directory.EnumerateFileSystemEntries(reload))
        {
            string name = Path.GetFileName(entry);
            if (!Directory.Exists(entry) || name is not ("state" or "shadow"))
            {
                throw new InvalidDataException($"Static runtime bundle reload directory contains an unexpected entry: {name}");
            }
        }
    }

    private static IEnumerable<string> EnumerateFilesBounded(string root)
    {
        var pending = new Stack<(string Directory, int Depth)>();
        pending.Push((root, 0));
        while (pending.Count > 0)
        {
            (string directory, int depth) = pending.Pop();
            foreach (string entry in Directory.EnumerateFileSystemEntries(directory))
            {
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

    public static string ResolveModuleDirectory(string bundleRoot)
    {
        string root = Path.GetFullPath(bundleRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (!Directory.Exists(root))
        {
            throw new DirectoryNotFoundException($"Runtime bundle does not exist: {root}");
        }

        var candidates = new List<string>();
        foreach (string directory in Directory.EnumerateDirectories(root, "modules.version.*", SearchOption.TopDirectoryOnly))
        {
            if (candidates.Count == MaxTreeEntries)
            {
                throw new InvalidDataException($"Runtime bundle exceeds the maximum of {MaxTreeEntries} module directory candidates.");
            }
            candidates.Add(directory);
        }
        if (candidates.Count != 1
            || !PathComparer.Equals(Path.GetFileName(candidates[0]), "modules.version.1")
            || !HasExactFile(Path.Combine(candidates[0], ".complete"), ModuleCompletionMarker))
        {
            throw new InvalidDataException($"Runtime bundle must contain only the exact completed module directory modules.version.1; found {candidates.Count} candidates.");
        }
        EnsureExistingPathHasNoReparsePoints(candidates[0]);
        return candidates[0];
    }

    public static string[] ReadCanonicalModuleManifest(string moduleDirectory)
    {
        string modules = Path.GetFullPath(moduleDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (!Directory.Exists(modules))
        {
            throw new DirectoryNotFoundException($"Runtime module directory does not exist: {modules}");
        }
        EnsureExistingPathHasNoReparsePoints(modules);
        RequireExact(Path.Combine(modules, ".complete"), ModuleCompletionMarker, "module completion marker");

        string listPath = Path.Combine(modules, "modules.list");
        byte[] bytes = ReadBoundedFile(listPath, MaxManifestBytes, "module manifest");
        string text;
        try
        {
            text = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true).GetString(bytes);
        }
        catch (DecoderFallbackException exception)
        {
            throw new InvalidDataException("Runtime module manifest must be valid UTF-8 without a BOM.", exception);
        }
        if (bytes.AsSpan().StartsWith(Encoding.UTF8.Preamble) || !text.EndsWith('\n') || text.Contains('\r'))
        {
            throw new InvalidDataException("Runtime module manifest must use canonical UTF-8 bytes and LF line endings.");
        }

        string[] names = text[..^1].Split('\n');
        if (names.Length is 0 or > MaxManifestEntries
            || names.Any(string.IsNullOrEmpty)
            || !names.SequenceEqual(names.Order(StringComparer.Ordinal), StringComparer.Ordinal))
        {
            throw new InvalidDataException("Runtime module manifest must be non-empty, bounded, unique, and ordinally sorted.");
        }
        var unique = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string name in names)
        {
            if (name != Path.GetFileName(name)
                || !name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
                || !unique.Add(name))
            {
                throw new InvalidDataException($"Invalid runtime module manifest entry: {name}");
            }
            if (name.StartsWith("Karpik.Engine.Core.Runner", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("A game-owned runtime bundle must not contain the engine runner.");
            }
        }

        string canonical = string.Join('\n', names) + '\n';
        if (!bytes.AsSpan().SequenceEqual(Encoding.UTF8.GetBytes(canonical)))
        {
            throw new InvalidDataException("Runtime module manifest bytes are not canonical.");
        }

        var actualDlls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int entries = 0;
        long totalBytes = bytes.Length + Encoding.UTF8.GetByteCount(ModuleCompletionMarker);
        foreach (string entry in Directory.EnumerateFileSystemEntries(modules))
        {
            if (++entries > MaxManifestEntries + 2 || IsReparsePoint(entry) || Directory.Exists(entry))
            {
                throw new InvalidDataException("Runtime module directory contains an unexpected, linked, or excessive entry.");
            }
            string fileName = Path.GetFileName(entry);
            if (fileName is ".complete" or "modules.list" or EngineModuleManifestFileName)
            {
                continue;
            }
            if (!fileName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
                || fileName.StartsWith("Karpik.Engine.Core.Runner", StringComparison.OrdinalIgnoreCase)
                || !actualDlls.Add(fileName))
            {
                throw new InvalidDataException($"Runtime module directory contains an unexpected file: {fileName}");
            }
            long length = new FileInfo(entry).Length;
            if (length > MaxIndividualFileBytes || totalBytes > MaxBundleBytes - length)
            {
                throw new InvalidDataException("Runtime module directory exceeds file or aggregate byte bounds.");
            }
            totalBytes += length;
        }
        if (!actualDlls.SetEquals(names))
        {
            throw new InvalidDataException("Runtime module manifest and module DLL set do not match exactly.");
        }
        return names;
    }

    public static string[] ReadCanonicalEngineModuleManifest(string moduleDirectory)
    {
        string modules = Path.GetFullPath(moduleDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        EnsureExistingPathHasNoReparsePoints(modules);
        byte[] bytes = ReadBoundedFile(
            Path.Combine(modules, EngineModuleManifestFileName),
            MaxManifestBytes,
            "engine module selection manifest");
        string text;
        try
        {
            text = new UTF8Encoding(false, true).GetString(bytes);
        }
        catch (DecoderFallbackException exception)
        {
            throw new InvalidDataException("Engine module selection manifest must be valid UTF-8 without a BOM.", exception);
        }
        if (bytes.AsSpan().StartsWith(Encoding.UTF8.Preamble) || !text.EndsWith('\n') || text.Contains('\r'))
        {
            throw new InvalidDataException("Engine module selection manifest must use canonical UTF-8 bytes and LF line endings.");
        }

        string[] ids = text[..^1].Split('\n');
        var unique = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (ids.Length is 0 or > MaxManifestEntries ||
            !ids.SequenceEqual(ids.Order(StringComparer.Ordinal), StringComparer.Ordinal) ||
            ids.Any(id => !IsSafeEngineModuleId(id) || !unique.Add(id)))
        {
            throw new InvalidDataException("Engine module selection manifest must be non-empty, bounded, unique, safe, and ordinally sorted.");
        }
        string canonical = string.Join('\n', ids) + '\n';
        if (!bytes.AsSpan().SequenceEqual(Encoding.UTF8.GetBytes(canonical)))
        {
            throw new InvalidDataException("Engine module selection manifest bytes are not canonical.");
        }
        return ids;
    }

    private static bool IsSafeEngineModuleId(string id) =>
        !string.IsNullOrWhiteSpace(id) && char.IsAsciiLetterOrDigit(id[0]) && char.IsAsciiLetterOrDigit(id[^1]) &&
        id.All(character => char.IsAsciiLetterOrDigit(character) || character is '.' or '-' or '_');

    internal static bool IsReparsePoint(string path)
    {
        var info = Directory.Exists(path) ? (FileSystemInfo)new DirectoryInfo(path) : new FileInfo(path);
        return (info.Attributes & FileAttributes.ReparsePoint) != 0 || info.LinkTarget is not null;
    }

    internal static void EnsureExistingPathHasNoReparsePoints(string path)
    {
        FileSystemInfo? current = Directory.Exists(path) ? new DirectoryInfo(path) : new FileInfo(path);
        while (current is not null)
        {
            if (IsReparsePoint(current.FullName))
            {
                throw new InvalidDataException($"Runtime path contains a link or reparse point: {current.FullName}");
            }
            current = current switch
            {
                FileInfo file => file.Directory,
                DirectoryInfo directory => directory.Parent,
                _ => null
            };
        }
    }

    internal static bool IsBoundedTreeWithoutReparsePoints(string root)
    {
        try
        {
            EnsureTreeIsBoundedAndHasNoReparsePoints(root);
            return true;
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static void ValidateRootShape(string root)
    {
        foreach (string entry in Directory.EnumerateFileSystemEntries(root))
        {
            string name = Path.GetFileName(entry);
            if (File.Exists(entry) && name is ".complete" or "runtime-bundle.side")
            {
                continue;
            }
            if (Directory.Exists(entry) && name is "Content" or "Mods" or "modules.version.1" or "reload")
            {
                continue;
            }
            throw new InvalidDataException($"Runtime bundle contains an unexpected root entry: {name}");
        }

        string reload = Path.Combine(root, "reload");
        if (!Directory.Exists(reload))
        {
            return;
        }
        foreach (string entry in Directory.EnumerateFileSystemEntries(reload))
        {
            string name = Path.GetFileName(entry);
            if (!Directory.Exists(entry) || name is not ("state" or "shadow"))
            {
                throw new InvalidDataException($"Runtime bundle reload directory contains an unexpected entry: {name}");
            }
        }
    }

    private static void EnsureTreeIsBoundedAndHasNoReparsePoints(string root)
    {
        if (IsReparsePoint(root))
        {
            throw new InvalidDataException($"Runtime bundle root is a link/reparse point: {root}");
        }
        var pending = new Stack<(string Directory, int Depth)>();
        pending.Push((root, 0));
        int entries = 0;
        long totalBytes = 0;
        while (pending.Count > 0)
        {
            (string directory, int depth) = pending.Pop();
            foreach (string entry in Directory.EnumerateFileSystemEntries(directory))
            {
                if (++entries > MaxTreeEntries || IsReparsePoint(entry))
                {
                    throw new InvalidDataException("Runtime bundle exceeds entry bounds or contains a link/reparse point.");
                }
                int entryDepth = depth + 1;
                if (entryDepth > MaxTreeDepth)
                {
                    throw new InvalidDataException($"Runtime bundle exceeds maximum tree depth {MaxTreeDepth}.");
                }
                if (Directory.Exists(entry))
                {
                    pending.Push((entry, entryDepth));
                    continue;
                }
                long length = new FileInfo(entry).Length;
                if (length > MaxIndividualFileBytes || totalBytes > MaxBundleBytes - length)
                {
                    throw new InvalidDataException("Runtime bundle exceeds file or aggregate byte bounds.");
                }
                totalBytes += length;
            }
        }
    }

    private static void RequireExact(string path, string expected, string description)
    {
        if (!HasExactFile(path, expected))
        {
            throw new InvalidDataException($"Runtime {description} is missing or incompatible: {path}");
        }
    }

    private static bool HasExactFile(string path, string expected)
    {
        if (!File.Exists(path) || IsReparsePoint(path))
        {
            return false;
        }
        byte[] expectedBytes = Encoding.UTF8.GetBytes(expected);
        var info = new FileInfo(path);
        return info.Length == expectedBytes.Length
               && File.ReadAllBytes(path).AsSpan().SequenceEqual(expectedBytes);
    }

    private static byte[] ReadBoundedFile(string path, int maxBytes, string description)
    {
        if (!File.Exists(path) || IsReparsePoint(path))
        {
            throw new InvalidDataException($"Runtime {description} is missing or linked: {path}");
        }
        var info = new FileInfo(path);
        if (info.Length is <= 0 or > int.MaxValue || info.Length > maxBytes)
        {
            throw new InvalidDataException($"Runtime {description} exceeds {maxBytes} bytes or is empty.");
        }
        byte[] bytes = File.ReadAllBytes(path);
        if (bytes.Length is 0 || bytes.Length > maxBytes)
        {
            throw new InvalidDataException($"Runtime {description} changed while being read or exceeds {maxBytes} bytes.");
        }
        return bytes;
    }
}
