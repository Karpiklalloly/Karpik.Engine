using Karpik.Engine.Tooling;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

namespace Karpik.Engine.Sdk.Tasks;

public sealed class ResolveKarpikStaticReferencesTask : Microsoft.Build.Utilities.Task
{
    [Required]
    public string EngineRoot { get; set; } = "";

    [Required]
    public string Side { get; set; } = "";

    [Output]
    public ITaskItem[] References { get; set; } = [];

    public override bool Execute()
    {
        References = [];
        try
        {
            References = Resolve();
            return true;
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or InvalidDataException or
                                           UnauthorizedAccessException or NotSupportedException)
        {
            Log.LogError($"KARPIK011: Static module reference resolution failed: {exception.Message}");
            return false;
        }
    }

    private ITaskItem[] Resolve()
    {
        if (!Path.IsPathFullyQualified(EngineRoot))
        {
            throw new ArgumentException("KarpikEngineRoot must be an absolute path.", nameof(EngineRoot));
        }

        EngineModuleSide side = ParseSide(Side);
        string engineRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(EngineRoot));
        EnsureNotReparse(engineRoot);
        string modulesRoot = Path.Combine(engineRoot, "modules");
        if (!Directory.Exists(modulesRoot))
        {
            throw new DirectoryNotFoundException($"Engine modules directory is missing: {modulesRoot}");
        }
        EnsureNotReparse(modulesRoot);

        EngineModuleCatalogEntry[] catalog = EngineModuleCatalog.Read(modulesRoot);
        IEnumerable<EngineModuleCatalogEntry> selected = side == EngineModuleSide.Shared
            ? catalog.Where(entry => entry.Side == EngineModuleSide.Shared)
            : EngineModuleCatalog.ForSide(catalog, side);

        return selected
            .Select(entry => ResolvePrimaryAssembly(modulesRoot, entry.ModuleId))
            .Select(path => (ITaskItem)new TaskItem(path))
            .ToArray();
    }

    private static EngineModuleSide ParseSide(string side) => side switch
    {
        "Shared" => EngineModuleSide.Shared,
        "Client" => EngineModuleSide.Client,
        "Server" => EngineModuleSide.Server,
        _ => throw new ArgumentException("KarpikSide must be exactly Shared, Client, or Server.", nameof(side))
    };

    private static string ResolvePrimaryAssembly(string modulesRoot, string moduleId)
    {
        if (!ModuleLayoutPolicy.IsSafeModuleId(moduleId))
        {
            throw new InvalidDataException($"Engine module ID is unsafe: {moduleId}");
        }

        string moduleDirectory = Path.GetFullPath(Path.Combine(modulesRoot, moduleId));
        if (!IsContained(modulesRoot, moduleDirectory))
        {
            throw new InvalidDataException($"Engine module path escapes the modules directory: {moduleId}");
        }
        EnsureNotReparse(moduleDirectory);

        string assemblyPath = Path.GetFullPath(Path.Combine(
            moduleDirectory,
            ModuleLayoutPolicy.GetPrimaryAssemblyFileName(moduleId)));
        if (!IsContained(modulesRoot, assemblyPath) || !File.Exists(assemblyPath) || IsReparsePoint(assemblyPath))
        {
            throw new InvalidDataException($"Engine module primary assembly is missing, linked, or outside modules: {assemblyPath}");
        }
        EnsureNotReparse(Path.GetDirectoryName(assemblyPath)!);
        return assemblyPath;
    }

    private static void EnsureNotReparse(string path)
    {
        for (DirectoryInfo? current = new DirectoryInfo(Path.GetFullPath(path)); current is not null; current = current.Parent)
        {
            if (IsReparsePoint(current))
            {
                throw new InvalidDataException($"Engine module path contains a link or reparse point: {current.FullName}");
            }
        }
    }

    private static bool IsContained(string root, string candidate)
    {
        string prefix = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)) + Path.DirectorySeparatorChar;
        return Path.GetFullPath(candidate).StartsWith(prefix, PathComparison);
    }

    private static bool IsReparsePoint(FileSystemInfo info) =>
        (info.Attributes & FileAttributes.ReparsePoint) != 0 || info.LinkTarget is not null;

    private static bool IsReparsePoint(string path)
    {
        FileSystemInfo info = Directory.Exists(path) ? new DirectoryInfo(path) : new FileInfo(path);
        return IsReparsePoint(info);
    }

    private static StringComparison PathComparison => OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;
}
