namespace Karpik.Engine.Core;

public sealed record RuntimeLaunchOptions
{
    public Side Side { get; }
    public string RunnerExecutablePath { get; }
    public string BundlePath { get; }

    public RuntimeLaunchOptions(Side side, string runnerExecutablePath, string bundlePath)
    {
        if (side is not (Side.Client or Side.Server))
        {
            throw new ArgumentOutOfRangeException(nameof(side), side, "Runtime side must be Client or Server.");
        }
        RunnerExecutablePath = ValidateExistingAbsoluteFile(runnerExecutablePath, nameof(runnerExecutablePath));
        BundlePath = RuntimeBundleLayout.Validate(bundlePath, side);
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
        if (RuntimeBundleLayout.IsReparsePoint(fullPath))
        {
            throw new InvalidDataException($"Runner executable must not be a link or reparse point: {fullPath}");
        }
        return fullPath;
    }
}

public static class RuntimeBundleLayout
{
    public const string BundleCompletionMarker = "karpik-runtime-bundle-v1\n";
    public const string ModuleCompletionMarker = "karpik-module-staging-v1\n";
    public const string SideMarkerPrefix = "karpik-runtime-side-v1:";

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
        EnsureTreeHasNoReparsePoints(root);
        RequireExact(Path.Combine(root, ".complete"), BundleCompletionMarker, "bundle completion marker");
        RequireExact(Path.Combine(root, "runtime-bundle.side"), SideMarkerPrefix + side + "\n", "runtime side marker");

        string content = Path.Combine(root, "Content");
        if (!Directory.Exists(content) || !Directory.EnumerateFiles(content, "*", SearchOption.AllDirectories).Any())
        {
            throw new InvalidDataException("Runtime bundle Content is missing or empty.");
        }

        string modules = ResolveModuleDirectory(root);
        RequireExact(Path.Combine(modules, ".complete"), ModuleCompletionMarker, "module completion marker");
        string listPath = Path.Combine(modules, "modules.list");
        if (!File.Exists(listPath))
        {
            throw new InvalidDataException("Runtime bundle module manifest is missing.");
        }
        string[] names = File.ReadAllLines(listPath);
        if (names.Length == 0 || !names.SequenceEqual(names.Order(StringComparer.Ordinal), StringComparer.Ordinal))
        {
            throw new InvalidDataException("Runtime bundle module manifest must be non-empty and ordinally sorted.");
        }
        var unique = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string name in names)
        {
            if (name != Path.GetFileName(name) || !name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) || !unique.Add(name))
            {
                throw new InvalidDataException($"Invalid runtime module manifest entry: {name}");
            }
            if (name.StartsWith("Karpik.Engine.Core.Runner", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("A game-owned runtime bundle must not contain the engine runner.");
            }
            if (!File.Exists(Path.Combine(modules, name)))
            {
                throw new FileNotFoundException($"Runtime module listed in the manifest is missing: {name}", Path.Combine(modules, name));
            }
        }
        return root;
    }

    public static string ResolveModuleDirectory(string bundleRoot)
    {
        string[] candidates = Directory.GetDirectories(bundleRoot, "modules.version.*", SearchOption.TopDirectoryOnly)
            .Where(directory => File.Exists(Path.Combine(directory, ".complete")))
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (candidates.Length != 1)
        {
            throw new InvalidDataException($"Runtime bundle must contain exactly one completed module staging directory; found {candidates.Length}.");
        }
        return candidates[0];
    }

    internal static bool IsReparsePoint(string path)
    {
        var info = Directory.Exists(path) ? (FileSystemInfo)new DirectoryInfo(path) : new FileInfo(path);
        return (info.Attributes & FileAttributes.ReparsePoint) != 0 || info.LinkTarget is not null;
    }

    private static void EnsureTreeHasNoReparsePoints(string root)
    {
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            string directory = pending.Pop();
            if (IsReparsePoint(directory))
            {
                throw new InvalidDataException($"Runtime bundle contains a linked directory: {directory}");
            }
            foreach (string entry in Directory.EnumerateFileSystemEntries(directory))
            {
                if (IsReparsePoint(entry))
                {
                    throw new InvalidDataException($"Runtime bundle contains a link or reparse point: {entry}");
                }
                if (Directory.Exists(entry))
                {
                    pending.Push(entry);
                }
            }
        }
    }

    private static void RequireExact(string path, string expected, string description)
    {
        if (!File.Exists(path) || File.ReadAllText(path) != expected)
        {
            throw new InvalidDataException($"Runtime {description} is missing or incompatible: {path}");
        }
    }
}
