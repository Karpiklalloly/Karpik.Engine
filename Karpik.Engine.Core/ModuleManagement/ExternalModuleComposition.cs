using System.Reflection;
using Karpik.Engine.Core;
using Karpik.Engine.Core.ModuleManagement;

public sealed partial class ModuleLoader
{
    private readonly EngineModuleDescriptor[] _engineModules = [];
    private readonly string? _engineNativeRoot;

    public readonly record struct EngineModuleDescriptor(string ModuleId, string DirectoryPath);

    public ModuleLoader(
        string bundleRoot,
        IEnumerable<EngineModuleDescriptor> engineModules,
        string? engineNativeRoot = null) : this(bundleRoot)
    {
        ArgumentNullException.ThrowIfNull(engineModules);
        _engineModules = engineModules
            .OrderBy(module => module.ModuleId, StringComparer.Ordinal)
            .Select(ValidateEngineModule)
            .ToArray();
        if (engineNativeRoot is not null)
        {
            if (!Path.IsPathFullyQualified(engineNativeRoot) || !Directory.Exists(engineNativeRoot))
                throw new DirectoryNotFoundException($"Engine native root does not exist: {engineNativeRoot}");
            _engineNativeRoot = Path.GetFullPath(engineNativeRoot);
            RuntimeBundleLayout.EnsureExistingPathHasNoReparsePoints(_engineNativeRoot);
        }
    }

    private PluginLoadContext CreateLoadContext(string shadowCopyDirectory)
    {
        string[] dependencyDirectories =
            [shadowCopyDirectory, .. _engineModules.Select(module => module.DirectoryPath)];
        return new PluginLoadContext(
            shadowCopyDirectory,
            _bundleRoot,
            allowAppContextFallback: _bundleRoot is null,
            dependencyDirectories: dependencyDirectories,
            nativeDirectories: _engineNativeRoot is null ? null : [_engineNativeRoot]);
    }

    private Assembly[] LoadComposedAssemblies(string[] gameAssemblyNames)
    {
        var loaded = new List<Assembly>(_engineModules.Length + gameAssemblyNames.Length);
        foreach (EngineModuleDescriptor module in _engineModules)
            loaded.Add(LoadPrimaryAssembly(module.ModuleId, Path.Combine(module.DirectoryPath, module.ModuleId + ".dll")));
        foreach (string name in gameAssemblyNames)
            loaded.Add(LoadPrimaryAssembly(name, Path.Combine(_shadowCopyDirectory!, name + ".dll")));
        return loaded.ToArray();
    }

    private Assembly LoadPrimaryAssembly(string expectedName, string path)
    {
        AssemblyName identity = AssemblyName.GetAssemblyName(path);
        if (!string.Equals(identity.Name, expectedName, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Primary module assembly identity '{identity.Name}' does not match '{expectedName}'.");
        if (PluginLoadContext.IsSharedAssemblyName(identity.Name))
            throw new InvalidDataException($"Runtime module composition must identity-share '{identity.Name}' with the runner.");
        Assembly? existing = _loadContext!.Assemblies.FirstOrDefault(assembly =>
            string.Equals(assembly.GetName().FullName, identity.FullName, StringComparison.Ordinal));
        return existing ?? _loadContext.LoadFromAssemblyPath(path);
    }

    private static EngineModuleDescriptor ValidateEngineModule(EngineModuleDescriptor module)
    {
        if (string.IsNullOrWhiteSpace(module.ModuleId) || module.ModuleId != Path.GetFileName(module.ModuleId))
            throw new InvalidDataException($"Invalid engine module ID: {module.ModuleId}");
        if (!Path.IsPathFullyQualified(module.DirectoryPath) || !Directory.Exists(module.DirectoryPath))
            throw new DirectoryNotFoundException($"Engine module directory does not exist: {module.DirectoryPath}");
        string directory = Path.GetFullPath(module.DirectoryPath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        RuntimeBundleLayout.EnsureExistingPathHasNoReparsePoints(directory);
        string primary = Path.Combine(directory, module.ModuleId + ".dll");
        if (!File.Exists(primary))
            throw new FileNotFoundException($"Engine module primary assembly is missing: {primary}", primary);
        return module with { DirectoryPath = directory };
    }
}
