using System.Text.Json;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

namespace Karpik.Engine.Sdk.Tasks;

/// <summary>Resolves build-only analyzers belonging to referenced owners.</summary>
public sealed class ResolveKarpikCodegenTask : Microsoft.Build.Utilities.Task
{
    public string EngineRoot { get; set; } = "";
    public string SdkRoot { get; set; } = "";
    public ITaskItem[] ReferencedAssemblies { get; set; } = [];
    public ITaskItem[] SelectedModuleIds { get; set; } = [];
    public bool RespectModuleSelection { get; set; } = true;
    [Output]
    public ITaskItem[] Analyzers { get; set; } = [];

    public override bool Execute()
    {
        Analyzers = [];
        try
        {
            string root = string.IsNullOrWhiteSpace(EngineRoot) ? SdkRoot : EngineRoot;
            string catalog = Path.Combine(root, "codegen", "catalog.json");
            if (!File.Exists(catalog) && !string.IsNullOrEmpty(SdkRoot))
            {
                root = SdkRoot;
                catalog = Path.Combine(root, "codegen", "catalog.json");
            }
            if (!File.Exists(catalog)) return true;
            if (!Path.IsPathFullyQualified(root)) throw new InvalidDataException("Codegen root must be absolute.");
            EnsureNotLinked(catalog);
            if (new FileInfo(catalog).Length > 1024 * 1024) throw new InvalidDataException("Codegen catalog exceeds 1 MiB.");
            var document = JsonSerializer.Deserialize<Catalog>(File.ReadAllText(catalog));
            if (document is not { Version: 1, Generators: not null })
                throw new InvalidDataException("Unsupported codegen catalog version or missing Generators.");
            var owners = ReferencedAssemblies.Select(item => Path.GetFileNameWithoutExtension(item.ItemSpec))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var modules = SelectedModuleIds.Select(item => item.ItemSpec).ToHashSet(StringComparer.Ordinal);
            var paths = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            string codegenRoot = Path.GetFullPath(Path.Combine(root, "codegen")) + Path.DirectorySeparatorChar;
            foreach (var generator in document.Generators)
            {
                if (generator is null || string.IsNullOrWhiteSpace(generator.OwnerAssembly) || string.IsNullOrWhiteSpace(generator.AssemblyPath))
                    throw new InvalidDataException("Codegen registration requires OwnerAssembly and AssemblyPath.");
                string assembly = Path.GetFullPath(Path.Combine(root, generator.AssemblyPath));
                if (!assembly.StartsWith(codegenRoot, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)
                    || !assembly.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException($"Codegen path escapes its directory: {generator.AssemblyPath}");
                if (!owners.Contains(generator.OwnerAssembly) ||
                    (RespectModuleSelection && generator.ModuleId != null && !modules.Contains(generator.ModuleId))) continue;
                if (!File.Exists(assembly)) throw new FileNotFoundException("Registered codegen assembly is missing.", assembly);
                EnsureNotLinked(assembly);
                paths.Add(assembly);
            }
            Analyzers = paths.Select(path => (ITaskItem)new TaskItem(path)).ToArray();
            return true;
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or JsonException or NotSupportedException)
        {
            Log.LogError($"KARPIK014: Codegen resolution failed: {error.Message}");
            return false;
        }
    }

    private static void EnsureNotLinked(string path)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException($"Codegen path is linked: {path}");
        for (var directory = new DirectoryInfo(Path.GetDirectoryName(path)!); directory != null; directory = directory.Parent)
            if ((directory.Attributes & FileAttributes.ReparsePoint) != 0 || directory.LinkTarget != null)
                throw new InvalidDataException($"Codegen directory is linked: {directory.FullName}");
    }

    private sealed record Catalog(int Version, Registration[] Generators);
    private sealed record Registration(string OwnerAssembly, string? ModuleId, string AssemblyPath);
}
