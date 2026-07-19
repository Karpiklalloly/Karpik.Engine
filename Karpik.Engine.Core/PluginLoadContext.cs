using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using System.Security.Cryptography;

namespace Karpik.Engine.Core.ModuleManagement;

public class PluginLoadContext : AssemblyLoadContext
{
    private readonly string _shadowCopyDirectory;
    private readonly string? _bundleRoot;
    private readonly bool _allowAppContextFallback;
    private readonly IReadOnlyDictionary<string, string[]> _managedAssemblyPaths;
    private readonly string[] _dependencyDirectories;
    private readonly string[] _nativeDirectories;
    
    private static readonly HashSet<string> SharedAssemblyNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Karpik.Engine.Core.Runner",
        "Karpik.Engine.Core",
        "Dragon",
        "Karpik.Jobs",
    };

    public PluginLoadContext(
        string shadowCopyDirectory,
        string? bundleRoot = null,
        bool allowAppContextFallback = true,
        IEnumerable<string>? dependencyDirectories = null,
        IEnumerable<string>? nativeDirectories = null) : base(isCollectible: bundleRoot is not null)
    {
        _shadowCopyDirectory = shadowCopyDirectory;
        _bundleRoot = bundleRoot;
        _allowAppContextFallback = allowAppContextFallback;
        _dependencyDirectories = dependencyDirectories?.Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase).ToArray() ?? [];
        _nativeDirectories = nativeDirectories?.Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase).ToArray() ?? [];
        _managedAssemblyPaths = BuildManagedAssemblyIndex(_dependencyDirectories);
    }

    public static bool IsSharedAssemblyName(string? assemblyName) =>
        SharedAssemblyNames.Contains(assemblyName ?? string.Empty);

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        if (SharedAssemblyNames.Contains(assemblyName.Name ?? string.Empty))
        {
            return null;
        }
        
        if (_managedAssemblyPaths.TryGetValue(assemblyName.Name ?? string.Empty, out string[]? indexedPaths))
        {
            string? compatible = indexedPaths.FirstOrDefault(path =>
                string.Equals(AssemblyName.GetAssemblyName(path).FullName, assemblyName.FullName, StringComparison.Ordinal));
            if (compatible is not null)
            {
                return LoadFromAssemblyPath(compatible);
            }
            throw new FileLoadException(
                $"No deterministic engine dependency matches requested identity '{assemblyName.FullName}'. " +
                $"Candidates: {string.Join(", ", indexedPaths.Select(path => AssemblyName.GetAssemblyName(path).FullName))}");
        }

        string libraryName = assemblyName.Name + ".dll";
        var searchPaths = CandidatePaths(libraryName, native: false);
        
        foreach (var path in searchPaths)
        {
            if (File.Exists(path))
            {
                return LoadFromAssemblyPath(path);
            }
        }

        return null; 
    }

    protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
    {
        foreach (string path in NativeCandidatePaths(unmanagedDllName))
        {
            if (!File.Exists(path)) continue;
            if (NativeLibrary.TryLoad(path, out var handle))
            {
                return handle;
            }
        }

        return IntPtr.Zero;
    }

    internal IEnumerable<string> NativeCandidatePaths(string unmanagedDllName)
    {
        foreach (string fileName in NativeLibraryFileNames(unmanagedDllName))
        {
            foreach (string path in CandidatePaths(fileName, native: true))
                yield return path;
            foreach (string directory in _nativeDirectories)
            {
                yield return Path.Combine(directory, fileName);
                yield return Path.Combine(directory, "runtimes", RuntimeInformation.RuntimeIdentifier, "native", fileName);
            }
        }
    }

    private static IEnumerable<string> NativeLibraryFileNames(string name)
    {
        yield return name;
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            if (!name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                yield return name + ".dll";
            yield break;
        }
        string prefix = name.StartsWith("lib", StringComparison.Ordinal) ? string.Empty : "lib";
        string extension = RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? ".dylib" : ".so";
        if (!name.EndsWith(extension, StringComparison.Ordinal))
            yield return prefix + name + extension;
    }

    private IEnumerable<string> CandidatePaths(string libraryName, bool native)
    {
        yield return Path.Combine(_shadowCopyDirectory, libraryName);
        if (native)
        {
            yield return Path.Combine(_shadowCopyDirectory, "runtimes", RuntimeInformation.RuntimeIdentifier, "native", libraryName);
        }
        if (_bundleRoot is not null)
        {
            yield return Path.Combine(_bundleRoot, libraryName);
            if (native)
            {
                yield return Path.Combine(_bundleRoot, "runtimes", RuntimeInformation.RuntimeIdentifier, "native", libraryName);
            }
        }
        foreach (string directory in _dependencyDirectories)
        {
            yield return Path.Combine(directory, libraryName);
            if (native)
            {
                yield return Path.Combine(directory, "runtimes", RuntimeInformation.RuntimeIdentifier, "native", libraryName);
                yield return Path.Combine(directory, "native", RuntimeInformation.RuntimeIdentifier, libraryName);
            }
        }
        if (!_allowAppContextFallback)
        {
            yield break;
        }
        yield return Path.Combine(AppContext.BaseDirectory, libraryName);
        if (native)
        {
            yield return Path.Combine(AppContext.BaseDirectory, "modules", "runtimes", "win-x64", "native", libraryName);
            yield return Path.Combine(AppContext.BaseDirectory, "runtimes", RuntimeInformation.RuntimeIdentifier, "native", libraryName);
        }
    }

    private static IReadOnlyDictionary<string, string[]> BuildManagedAssemblyIndex(IEnumerable<string> directories)
    {
        var byName = new Dictionary<string, List<(string FullName, string Path, string Hash)>>(StringComparer.OrdinalIgnoreCase);
        foreach (string directory in directories.Order(StringComparer.Ordinal))
        {
            if (!Directory.Exists(directory))
            {
                continue;
            }
            foreach (string path in Directory.EnumerateFiles(directory, "*.dll", SearchOption.TopDirectoryOnly).Order(StringComparer.Ordinal))
            {
                AssemblyName identity;
                try
                {
                    identity = AssemblyName.GetAssemblyName(path);
                }
                catch (BadImageFormatException)
                {
                    continue;
                }
                string name = identity.Name ?? Path.GetFileNameWithoutExtension(path);
                string fullName = identity.FullName ?? name;
                using FileStream stream = File.OpenRead(path);
                string hash = Convert.ToHexString(SHA256.HashData(stream));
                if (!byName.TryGetValue(name, out List<(string FullName, string Path, string Hash)>? candidates))
                {
                    candidates = [];
                    byName.Add(name, candidates);
                }
                else if (candidates.Count > 0 && !string.Equals(candidates[0].FullName, fullName, StringComparison.Ordinal))
                {
                    throw new InvalidDataException(
                        $"Engine payload contains assemblies with the same simple name '{name}' but different identities: " +
                        $"'{candidates[0].FullName}' and '{fullName}'.");
                }
                var sameIdentity = candidates.FirstOrDefault(candidate => candidate.FullName == fullName);
                if (sameIdentity.Path is not null)
                {
                    if (!string.Equals(sameIdentity.Hash, hash, StringComparison.Ordinal))
                    {
                        throw new InvalidDataException($"Engine payload contains byte-distinct assemblies with the same identity '{fullName}'.");
                    }
                    continue;
                }
                candidates.Add((fullName, path, hash));
            }
        }
        return byName.ToDictionary(
            pair => pair.Key,
            pair => pair.Value.OrderBy(candidate => candidate.FullName, StringComparer.Ordinal).Select(candidate => candidate.Path).ToArray(),
            StringComparer.OrdinalIgnoreCase);
    }
}
