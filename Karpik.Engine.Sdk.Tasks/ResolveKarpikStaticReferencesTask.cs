using Karpik.Engine.Tooling;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using System.Reflection;

namespace Karpik.Engine.Sdk.Tasks;

/// <summary>
/// MSBuild-задача, собирающая безопасные ссылки на модули для статической композиции.
/// </summary>
public sealed class ResolveKarpikStaticReferencesTask : Microsoft.Build.Utilities.Task
{
    /// <summary>
    /// Получает абсолютный корень валидированной установки движка.
    /// </summary>
    [Required]
    public string EngineRoot { get; set; } = "";

    [Required]
    /// <summary>
    /// Получает сторону runtime-графа: <c>Shared</c>, <c>Client</c> или <c>Server</c>.
    /// </summary>
    public string Side { get; set; } = "";

    /// <summary>
    /// Получает ссылки на первичные сборки выбранных модулей вместе с CLR-идентичностью.
    /// </summary>
    [Output]
    public ITaskItem[] References { get; set; } = [];

    /// <summary>
    /// Every safe DLL that ships inside the resolved modules' own directories
    /// (third-party payloads such as Aether.Physics2D or Newtonsoft.Json), plus
    /// every safe DLL in the installation shared dependencies directory created
    /// by payload layout v3. Static compilations must reference these because
    /// generated composition factories mention service constructor parameter
    /// types transitively.
    /// </summary>
    [Output]
    public ITaskItem[] PayloadAssemblies { get; set; } = [];

    /// <summary>
    /// Разрешает ссылки на модули и их payload-сборки для заданной стороны.
    /// </summary>
    /// <returns><see langword="true"/>, если все ссылки разрешены; иначе <see langword="false"/>.</returns>
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

    /// <summary>
    /// Читает каталог модулей и формирует ссылки на первичные и payload-сборки.
    /// </summary>
    /// <param name="payloadAssemblies">Получает DLL-пакеты, лежащие рядом с первичными сборками.</param>
    /// <returns>Ссылки на первичные сборки модулей.</returns>
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
        HashSet<string> catalogPrimaryFileNames = catalog
            .Select(entry => ModuleLayoutPolicy.GetPrimaryAssemblyFileName(entry.ModuleId))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        IEnumerable<EngineModuleCatalogEntry> selected = side == EngineModuleSide.Shared
            ? catalog.Where(entry => entry.Side == EngineModuleSide.Shared)
            : EngineModuleCatalog.ForSide(catalog, side);

        List<ITaskItem> references = new List<ITaskItem>();
        List<ITaskItem> payloads = new List<ITaskItem>();
        HashSet<string> identities = new HashSet<string>(StringComparer.Ordinal);
        Dictionary<string, string> simpleNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        HashSet<string> payloadPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
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
            TaskItem reference = new TaskItem(path);
            reference.SetMetadata("AssemblyIdentity", identity);
            references.Add(reference);
        }

        payloadAssemblies = CollectSharedPayloadAssemblies(engineRoot, payloadPaths, catalogPrimaryFileNames, payloads);
        return [.. references];
    }

    /// <summary>
    /// Добавляет DLL из каталога общих зависимостей (payload layout v3).
    /// Каталог может отсутствовать в установках layout v2.
    /// </summary>
    private ITaskItem[] CollectSharedPayloadAssemblies(
        string engineRoot,
        HashSet<string> seen,
        HashSet<string> catalogPrimaryFileNames,
        List<ITaskItem> collected)
    {
        string sharedRoot = Path.Combine(engineRoot, "shared");
        if (!Directory.Exists(sharedRoot))
        {
            return [.. collected];
        }
        EnsureNotReparse(sharedRoot);
        foreach (string file in Directory.EnumerateFiles(sharedRoot, "*.dll", SearchOption.TopDirectoryOnly).Order(StringComparer.Ordinal))
        {
            string fileName = Path.GetFileName(file);
            if (!seen.Add(file)
                || catalogPrimaryFileNames.Contains(fileName)
                || fileName.StartsWith("Karpik.Engine.Core.Runner", StringComparison.OrdinalIgnoreCase)
                || IsReparsePoint(file))
            {
                continue;
            }

            collected.Add(new TaskItem(file));
        }
        return [.. collected];
    }

    /// <summary>
    /// Добавляет DLL модуля, кроме его первичной сборки и runner-сборок, в payload.
    /// </summary>
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

    /// <summary>
    /// Преобразует строковое значение стороны MSBuild в значение каталога модулей.
    /// </summary>
    private static EngineModuleSide ParseSide(string side) => side switch
    {
        "Shared" => EngineModuleSide.Shared,
        "Client" => EngineModuleSide.Client,
        "Server" => EngineModuleSide.Server,
        _ => throw new ArgumentException("KarpikSide must be exactly Shared, Client, or Server.", nameof(side))
    };

    /// <summary>
    /// Находит и проверяет первичную DLL модуля, не разрешая выход за каталог модулей.
    /// </summary>
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

    /// <summary>
    /// Отклоняет путь, если он или его предки являются ссылкой либо reparse point.
    /// </summary>
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

    /// <summary>
    /// Проверяет, находится ли кандидат внутри корневого каталога.
    /// </summary>
    private static bool IsContained(string root, string candidate)
    {
        string prefix = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)) + Path.DirectorySeparatorChar;
        return Path.GetFullPath(candidate).StartsWith(prefix, PathComparison);
    }

    /// <summary>
    /// Определяет, является ли файловая система ссылкой или reparse point.
    /// </summary>
    private static bool IsReparsePoint(FileSystemInfo info) =>
        (info.Attributes & FileAttributes.ReparsePoint) != 0 || info.LinkTarget is not null;

    /// <summary>
    /// Определяет, является ли путь ссылкой или reparse point.
    /// </summary>
    private static bool IsReparsePoint(string path)
    {
        FileSystemInfo info = Directory.Exists(path) ? new DirectoryInfo(path) : new FileInfo(path);
        return IsReparsePoint(info);
    }

    /// <summary>
    /// Получает платформенно-зависимое правило сравнения путей.
    /// </summary>
    private static StringComparison PathComparison => OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;
}
