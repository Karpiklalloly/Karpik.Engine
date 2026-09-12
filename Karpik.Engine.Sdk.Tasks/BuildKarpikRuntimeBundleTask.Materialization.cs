using Microsoft.Build.Framework;
using System.Text;

namespace Karpik.Engine.Sdk.Tasks;

public sealed partial class BuildKarpikRuntimeBundleTask
{
    /// <summary>Материализует dynamic bundle с manifest-файлом и набором управляемых модулей.</summary>
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
        SortedDictionary<string, string> sources = new SortedDictionary<string, string>(StringComparer.Ordinal);
        HashSet<string> assemblyNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
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
        HashSet<string> assetDirectories = new HashSet<string>(BundleIdentityComparer);
        List<(string Source, string Destination)> assetSources = new List<(string Source, string Destination)>();
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

    /// <summary>Материализует static bundle без managed module manifest и DLL модулей.</summary>
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

        List<(string Source, string Destination)> sources = new List<(string Source, string Destination)>();
        HashSet<string> directories = new HashSet<string>(BundleIdentityComparer);
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

    /// <summary>Проверяет, допустим ли относительный путь native payload.</summary>
    private static bool IsNativeRelativePath(string relativePath)
    {
        string firstSegment = relativePath.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries)[0];
        return firstSegment is "native" or "runtimes";
    }

    /// <summary>Проверяет и собирает файлы контента, модов или native payload для staging-каталога.</summary>
    private static void PrepareAssetRoot(
        IEnumerable<ITaskItem> items,
        string rootName,
        string assetRoot,
        ISet<string> assetDirectories,
        ICollection<(string Source, string Destination)> assetSources,
        ref long totalInputBytes)
    {
        HashSet<string> targets = new HashSet<string>(BundleIdentityComparer);
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

    /// <summary>Добавляет уникальную и безопасную runtime DLL в набор копируемых сборок.</summary>
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

}
