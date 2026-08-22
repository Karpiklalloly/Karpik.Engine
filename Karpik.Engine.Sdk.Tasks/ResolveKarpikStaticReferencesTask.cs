using Karpik.Engine.Tooling;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using System.Reflection;

namespace Karpik.Engine.Sdk.Tasks;

public sealed class ResolveKarpikStaticReferencesTask : Microsoft.Build.Utilities.Task
{
    [Required]
    public string EngineRoot { get; set; } = "";

    [Required]
    public string Side { get; set; } = "";

    [Output]
    public ITaskItem[] References { get; set; } = [];

    /// <summary>
    /// Every safe DLL that ships inside the resolved modules' own directories
    /// (third-party payloads such as Aether.Physics2D or Newtonsoft.Json). Static
    /// compilations must reference these because generated composition factories
    /// mention service constructor parameter types transitively.
    /// </summary>
    [Output]
    public ITaskItem[] PayloadAssemblies { get; set; } = [];

    public override bool Execute()
    {
        References = [];
        PayloadAssemblies = [];
        try
        {
            References = Resolve(out ITaskItem[] payloadAssemblies);
            PayloadAssemblies = payloadAssemblies;
            return true;
        }
        catch (Exception exception) when (exception is ArgumentException or BadImageFormatException or IOException or
                                           InvalidDataException or UnauthorizedAccessException or NotSupportedException)
        {
            Log.LogError($"KARPIK011: Static module reference resolution failed: {exception.Message}");
            return false;
        }
    }

    private ITaskItem[] Resolve(out ITaskItem[] payloadAssemblies)
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

        var references = new List<ITaskItem>();
        var payloads = new List<ITaskItem>();
        var identities = new HashSet<string>(StringComparer.Ordinal);
        var simpleNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var payloadPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (EngineModuleCatalogEntry entry in selected)
        {
            string path = ResolvePrimaryAssembly(modulesRoot, entry.ModuleId);
            CollectPayloadAssemblies(Path.GetDirectoryName(path)!, path, payloadPaths, payloads);
            AssemblyName assemblyName = AssemblyName.GetAssemblyName(path);
            string simpleName = assemblyName.Name
                ?? throw new InvalidDataException($"Engine module assembly has no simple identity: {path}");
            string identity = assemblyName.FullName
                ?? throw new InvalidDataException($"Engine module assembly has no full identity: {path}");
            if (!identities.Add(identity))
            {
                continue;
            }
            if (simpleNames.TryGetValue(simpleName, out string? existingIdentity))
            {
                throw new InvalidDataException(
                    $"Engine modules expose conflicting CLR assembly identities for '{simpleName}': " +
                    $"'{existingIdentity}' and '{identity}'.");
            }

            simpleNames.Add(simpleName, identity);
            var reference = new TaskItem(path);
            reference.SetMetadata("AssemblyIdentity", identity);
            references.Add(reference);
        }

        payloadAssemblies = [.. payloads];
        return [.. references];
    }

    private void CollectPayloadAssemblies(string moduleDirectory, string primaryAssemblyPath,
        HashSet<string> seen, List<ITaskItem> collected)
    {
        foreach (string file in Directory.EnumerateFiles(moduleDirectory, "*.dll", SearchOption.TopDirectoryOnly))
        {
            if (string.Equals(file, primaryAssemblyPath, PathComparison)
                || !seen.Add(file)
                || Path.GetFileName(file).StartsWith("Karpik.Engine.Core.Runner", StringComparison.OrdinalIgnoreCase)
                || IsReparsePoint(file))
            {
                continue;
            }

            collected.Add(new TaskItem(file));
        }
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
